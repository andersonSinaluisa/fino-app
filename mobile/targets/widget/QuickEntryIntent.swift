import AppIntents
import Foundation

/**
 * §29: "Registrar gasto en FINO" desde Atajos y desde Siri.
 *
 * POR QUÉ NO USA OpenURLIntent
 *
 * La primera versión devolvía `OpenURLIntent(URL(string: "fino:///registrar")!)`.
 * Estaba mal por dos razones independientes:
 *
 *   1. `OpenURLIntent` es iOS 18.0+, y este proyecto compila con deployment target
 *      15.1. Anotarlo `@available(iOS 17.0, *)` no lo arregla: es un error de
 *      compilación que habría tumbado la build de iOS entera.
 *   2. Aunque compilara, la documentación de Apple dice que `OpenURLIntent` "usa el
 *      soporte de universal links que ya tiene tu app". `fino:///registrar` es un
 *      custom scheme, no un universal link, así que no habría abierto la pantalla.
 *
 * QUÉ HACE EN SU LUGAR
 *
 * Deja una marca en el App Group que la app ya comparte con los widgets y pide al
 * sistema que traiga Fino al frente. La app la recoge al pasar a primer plano
 * (hooks/usePendingIntent.ts) y abre el sheet de registro rápido. Es el MISMO
 * mecanismo que usa la extensión de compartir, funciona desde iOS 16 y no depende
 * de ninguna API que Apple no haya pensado para esto.
 *
 * §29 es explícito en que esto "debe utilizar el mismo caso de uso de creación. No
 * crear una segunda lógica nativa independiente". Por eso este archivo no habla con
 * la API, no conoce el modelo de movimientos y no sabe qué es una cuenta de efectivo:
 * solo abre la app en el sitio correcto. De ahí en adelante corre el mismo sheet, que
 * llama al mismo endpoint, que llama a CreateQuickTransaction.
 */

/// Debe coincidir byte a byte con lib/quickEntry/pendingIntent.ios.ts.
private enum QuickEntryHandoff {
    static let appGroup = "group.app.fino.mobile"
    static let key = "pendingQuickEntryIntent"

    /// Se guarda el instante para poder descartar una marca vieja: si alguien pidió
    /// el atajo hace dos horas y abre Fino ahora por su cuenta, no debería
    /// encontrarse el sheet abierto sin haberlo pedido.
    static func mark(voice: Bool) {
        let payload: [String: Any] = [
            "mode": voice ? "voice" : "keypad",
            "requestedAt": Date().timeIntervalSince1970,
        ]

        UserDefaults(suiteName: appGroup)?.set(payload, forKey: key)
    }
}

@available(iOS 16.0, *)
struct RegistrarGastoIntent: AppIntent {
    static var title: LocalizedStringResource = "Registrar gasto en Fino"

    static var description = IntentDescription(
        "Abre Fino listo para anotar un gasto en efectivo."
    )

    /// La app pasa a primer plano: el registro necesita que la persona escriba el
    /// monto y confirme. Un intent que guardara solo, sin que nadie vea qué se
    /// guardó, contradiría §43 ("no autoguardar interpretaciones ambiguas").
    static var openAppWhenRun: Bool = true

    @MainActor
    func perform() async throws -> some IntentResult {
        QuickEntryHandoff.mark(voice: false)
        return .result()
    }
}

/// La variante por voz (§13), para poder decirle a Siri "dictar un gasto en Fino".
@available(iOS 16.0, *)
struct DictarGastoIntent: AppIntent {
    static var title: LocalizedStringResource = "Dictar un gasto en Fino"

    static var description = IntentDescription(
        "Abre Fino con el micrófono listo para dictar un gasto."
    )

    static var openAppWhenRun: Bool = true

    @MainActor
    func perform() async throws -> some IntentResult {
        QuickEntryHandoff.mark(voice: true)
        return .result()
    }
}

/// Las frases que Siri reconoce sin que la persona configure nada.
///
/// `applicationName` se resuelve al nombre de la app en tiempo de compilación, así
/// que las frases siguen funcionando aunque el nombre cambie. Se declaran pocas y
/// naturales a propósito: Apple limita cuántas expone, y una lista larga de variantes
/// hace que Siri acierte MENOS, no más.
@available(iOS 16.0, *)
struct FinoAppShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(
            intent: RegistrarGastoIntent(),
            phrases: [
                "Registrar un gasto en \(.applicationName)",
                "Anotar un gasto en \(.applicationName)",
                "Registrar efectivo en \(.applicationName)",
            ],
            shortTitle: "Registrar gasto",
            systemImageName: "plus.circle.fill"
        )

        AppShortcut(
            intent: DictarGastoIntent(),
            phrases: [
                "Dictar un gasto en \(.applicationName)",
            ],
            shortTitle: "Dictar gasto",
            systemImageName: "mic.circle.fill"
        )
    }
}
