import UIKit

/**
 * La extensión de compartir de Fino no reimplementa nada: no dibuja el selector
 * de cuenta, ni la autenticación, ni la vista previa del CSV/XLSX. Copia el
 * archivo compartido al contenedor del App Group que ya usa la extensión de
 * widgets, deja su URI `file://` en el mismo UserDefaults compartido que lee
 * `ExtensionStorage` desde JS, y avisa a la persona. Desde ahí, la app toma el
 * relevo exactamente donde lo tomaría un "Elegir archivo" normal en
 * cuentas/importar.tsx.
 *
 * LO QUE ESTA EXTENSIÓN NO HACE, Y POR QUÉ
 *
 * No intenta abrir la app. Las dos versiones anteriores sí lo intentaban y las
 * dos fallaban con el mismo síntoma -- una ventana en blanco que aparece y
 * desaparece sin que pase nada más:
 *
 *   1. Recorriendo la cadena de responders en busca de un `UIApplication` al que
 *      mandarle `openURL:`. Además de ser una zona gris para App Review, es
 *      llamar a una API no disponible en extensiones.
 *   2. `NSExtensionContext.open(_:completionHandler:)`. La documentación de
 *      Apple es explícita: "In iOS, the Today and iMessage app extension points
 *      support this method". Una Share Extension NO está en esa lista, así que
 *      la llamada no hace nada, el completion handler devuelve `false` y la
 *      extensión se cierra sin haber abierto nada.
 *
 * No hay una tercera forma soportada. Así que el relevo va al revés: la
 * extensión deja el archivo listo y la app lo recoge sola la próxima vez que
 * pasa a primer plano (hooks/usePendingShare.ts). Es el mismo camino que ya
 * usaban los widgets para compartir estado, y no depende de ninguna API que
 * Apple no haya pensado para esto.
 */
class ShareViewController: UIViewController {
  private static let appGroup = "group.app.fino.mobile"
  private static let pendingShareFileKey = "pendingShareFilePath"

  override func viewDidLoad() {
    super.viewDidLoad()
    handleSharedItem()
  }

  private func handleSharedItem() {
    guard
      let item = extensionContext?.inputItems.first as? NSExtensionItem,
      let provider = item.attachments?.first
    else {
      close()
      return
    }

    // "public.data" sits near the root of the UTI hierarchy -- virtually
    // every file attachment conforms to it, whatever concrete type a given
    // provider reports. The real accept/reject check (extension whitelist,
    // same as picking a file by hand) happens once the file is back in the
    // main app, in resolveSharedFile() -- this extension's job is only to
    // move the bytes somewhere the app can read them from.
    let typeIdentifier = "public.data"
    guard provider.hasItemConformingToTypeIdentifier(typeIdentifier) else {
      close()
      return
    }

    provider.loadItem(forTypeIdentifier: typeIdentifier, options: nil) { [weak self] item, error in
      self?.handleLoadedItem(item, error: error)
    }
  }

  private func handleLoadedItem(_ item: NSSecureCoding?, error: Error?) {
    guard error == nil else {
      close()
      return
    }

    var sourceURL: URL?
    if let url = item as? URL {
      sourceURL = url
    } else if let data = item as? Data {
      // Some providers hand back raw Data instead of a file URL -- write it
      // to a temp file ourselves so the copy step below only ever deals
      // with a URL, whichever path got us here.
      let tempURL = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
      do {
        try data.write(to: tempURL)
        sourceURL = tempURL
      } catch {
        sourceURL = nil
      }
    }

    guard let source = sourceURL else {
      close()
      return
    }

    copyIntoAppGroup(source: source)
  }

  private func copyIntoAppGroup(source: URL) {
    guard
      let containerURL = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: Self.appGroup)
    else {
      close()
      return
    }

    let inboxURL = containerURL.appendingPathComponent("ShareInbox", isDirectory: true)
    let fileName = source.lastPathComponent
    let destination = inboxURL.appendingPathComponent(fileName)

    do {
      try FileManager.default.createDirectory(at: inboxURL, withIntermediateDirectories: true)
      if FileManager.default.fileExists(atPath: destination.path) {
        try FileManager.default.removeItem(at: destination)
      }
      try FileManager.default.copyItem(at: source, to: destination)
    } catch {
      close()
      return
    }

    UserDefaults(suiteName: Self.appGroup)?.set(destination.absoluteString, forKey: Self.pendingShareFileKey)
    confirmAndClose(fileName: fileName)
  }

  /**
   * Confirma a la persona que el archivo quedó listo y le dice qué hacer.
   *
   * Antes esta extensión no dibujaba nada, y eso era justamente el problema: al
   * no poder abrir la app, lo único que se veía era un destello en blanco. Un
   * mensaje explícito convierte un fallo aparente en un paso entendible.
   */
  private func confirmAndClose(fileName: String) {
    DispatchQueue.main.async { [weak self] in
      guard let self else { return }

      self.view.backgroundColor = UIColor(red: 0.96, green: 0.95, blue: 0.93, alpha: 1)

      let title = UILabel()
      title.text = "Listo para importar"
      title.font = .systemFont(ofSize: 17, weight: .semibold)
      title.textColor = UIColor(red: 0.10, green: 0.10, blue: 0.09, alpha: 1)
      title.textAlignment = .center

      let subtitle = UILabel()
      subtitle.text = "Abre Fino para elegir la cuenta e importar \(fileName)."
      subtitle.font = .systemFont(ofSize: 14, weight: .regular)
      subtitle.textColor = UIColor(red: 0.45, green: 0.46, blue: 0.44, alpha: 1)
      subtitle.textAlignment = .center
      subtitle.numberOfLines = 0

      let stack = UIStackView(arrangedSubviews: [title, subtitle])
      stack.axis = .vertical
      stack.spacing = 8
      stack.translatesAutoresizingMaskIntoConstraints = false

      self.view.addSubview(stack)
      NSLayoutConstraint.activate([
        stack.centerXAnchor.constraint(equalTo: self.view.centerXAnchor),
        stack.centerYAnchor.constraint(equalTo: self.view.centerYAnchor),
        stack.leadingAnchor.constraint(greaterThanOrEqualTo: self.view.leadingAnchor, constant: 32),
        stack.trailingAnchor.constraint(lessThanOrEqualTo: self.view.trailingAnchor, constant: -32),
      ])

      // Tiempo suficiente para leerlo, poco suficiente para no estorbar.
      DispatchQueue.main.asyncAfter(deadline: .now() + 1.6) {
        self.close()
      }
    }
  }

  private func close() {
    extensionContext?.completeRequest(returningItems: nil, completionHandler: nil)
  }
}
