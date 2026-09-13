import SwiftUI
import WidgetKit

/// §28 ("widgets / acceso externo"): registrar un movimiento sin navegar por toda
/// Fino.
///
/// A diferencia de los otros ocho widgets, este no muestra ningún dato: es un botón.
/// Por eso no lee el snapshot compartido ni le importa si hay sesión iniciada --
/// abre la app en `fino:///registrar`, y es la propia app la que decide qué hacer
/// (abrir el sheet si hay sesión, mandar al login si no). Un widget nunca debe
/// contener la lógica de esa decisión.
///
/// Las dos zonas usan `Link` en vez de `.widgetURL` porque el widget tiene DOS
/// destinos: escribir y dictar. `.widgetURL` solo admite uno para todo el widget.
struct QuickEntryWidgetView: View {
    /// La misma dirección que usan los atajos de Android y el App Intent de iOS.
    /// Un solo camino de entrada externo, definido en lib/widgets/deepLinks.ts.
    private static let writeURL = URL(string: "fino:///registrar")
    private static let voiceURL = URL(string: "fino:///registrar?voz=1")

    var body: some View {
        VStack(spacing: 10) {
            if let writeURL = Self.writeURL {
                Link(destination: writeURL) {
                    HStack(spacing: 8) {
                        Image(systemName: "plus")
                            .font(.system(size: 18, weight: .bold))
                        Text("Registrar")
                            .font(.system(size: 15, weight: .semibold))
                    }
                    .foregroundStyle(Color("onAccent"))
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 12)
                    .background(Color("accentLime"), in: Capsule())
                }
            }

            if let voiceURL = Self.voiceURL {
                Link(destination: voiceURL) {
                    HStack(spacing: 6) {
                        Image(systemName: "mic.fill")
                            .font(.system(size: 12, weight: .semibold))
                        Text("Dictar")
                            .font(.system(size: 13, weight: .medium))
                    }
                    .foregroundStyle(Color("textSecondary"))
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 8)
                    .background(Color("surfaceSecondary"), in: Capsule())
                }
            }
        }
        .padding()
    }
}

struct QuickEntryWidget: Widget {
    let kind = "QuickEntryWidget"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: FinoProvider()) { _ in
            QuickEntryWidgetView()
                .containerBackground(Color("surface"), for: .widget)
        }
        .configurationDisplayName("Registrar efectivo")
        .description("Anota un gasto en efectivo sin abrir Fino entera.")
        .supportedFamilies([.systemSmall])
    }
}
