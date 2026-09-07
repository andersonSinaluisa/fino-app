import UIKit

/**
 * Fino's share extension is deliberately silent: it never draws its own UI
 * (that would mean reimplementing the account picker, auth, and the whole
 * CSV/XLSX preview natively in Swift). All it does is copy the shared file
 * into the App Group container the widget extension already uses, drop its
 * `file://` URI into the same shared UserDefaults suite `ExtensionStorage`
 * (lib/widgets/*) reads from the JS side, then hand off to the main app on
 * a deep link. From there app/compartir.tsx picks up exactly where a normal
 * "Elegir archivo" tap in cuentas/importar.tsx would.
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
    openMainApp(fileName: fileName)
  }

  private func openMainApp(fileName: String) {
    guard
      let encodedName = fileName.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed),
      let url = URL(string: "fino:///compartir?file=\(encodedName)")
    else {
      close()
      return
    }

    // Extensions have no `UIApplication.shared` of their own -- walking the
    // responder chain up to the host `UIApplication` and calling the classic
    // `openURL:` selector is the standard, still-working way a share
    // extension hands off to its main app.
    let openSelector = sel_registerName("openURL:")
    var responder: UIResponder? = self
    while let current = responder {
      if let application = current as? UIApplication, application.responds(to: openSelector) {
        application.perform(openSelector, with: url)
        break
      }
      responder = current.next
    }

    close()
  }

  private func close() {
    extensionContext?.completeRequest(returningItems: nil, completionHandler: nil)
  }
}
