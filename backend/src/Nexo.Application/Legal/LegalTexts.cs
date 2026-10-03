namespace Nexo.Application.Legal;

/// <summary>
/// Texto de la Política de privacidad y de los Términos y condiciones. Una
/// sola fuente: la app los pide por API y la misma versión se publica como
/// página web (para App Store / Google Play). Markdown simple: #, ##, -, **.
///
/// Si cambias el contenido de forma relevante, sube la versión en
/// LegalOptions (o en la configuración) para que todos acepten de nuevo.
///
/// Marcadores: {{RESPONSABLE}}, {{IDENTIFICACION}}, {{DIRECCION}},
/// {{CORREO}}, {{DELEGADO}}, {{HOSTING}}, {{VERSION}}, {{GRACIA}}.
/// Revisado contra la LOPDP y su Reglamento, la norma SPDP-SPD-2026-0004-R,
/// la Ley Orgánica de Defensa del Consumidor, la Ley de Comercio Electrónico
/// y la Ley Fintech (resolución JPRF-F-2025-0155). No reemplaza la revisión
/// de un abogado.
/// </summary>
internal static class LegalTexts
{
    public const string PrivacyTitle = "Política de privacidad";

    public const string TermsTitle = "Términos y condiciones";

    public const string Privacy = """
# Política de privacidad de Fino

Versión {{VERSION}}

Esta política explica qué datos personales trata Fino, para qué, con qué base legal, con quién se comparten, cuánto tiempo se guardan y cómo ejerces tus derechos, conforme a la Ley Orgánica de Protección de Datos Personales del Ecuador (LOPDP) y su Reglamento.

## 1. Responsable del tratamiento

- **Responsable:** {{RESPONSABLE}}
- **Cédula / RUC:** {{IDENTIFICACION}}
- **Dirección:** {{DIRECCION}}
- **Correo de privacidad, soporte y reclamos:** {{CORREO}}
- **Delegado de protección de datos:** {{DELEGADO}}

## 2. Qué es Fino

Fino es una app para organizar tus finanzas personales: reúne los movimientos de tus cuentas y tarjetas para que veas en qué gastas. Fino no es un banco ni una billetera, no mueve dinero, no se conecta a tu banca en línea con tus claves y no ofrece asesoría financiera.

## 3. Qué datos tratamos

**Datos de tu cuenta**
- Nombre, correo electrónico y contraseña (guardada solo como hash; nunca la vemos).
- Zona horaria, idioma y moneda.

**Datos financieros que tú cargas**
- Cuentas: nombre que les das, banco, últimos 4 dígitos y saldos.
- Movimientos: fecha, monto, descripción del banco, comercio, categoría, notas y divisiones.
- Presupuestos, reglas de categorización y estimaciones.
- Tarjetas de crédito: cupo, días de corte y pago, estados de cuenta y compras a cuotas. **Nunca** pedimos ni guardamos el número completo de la tarjeta, el CVV, el PIN ni tus claves de banca en línea.
- Estados de cuenta que importas: el archivo se procesa para extraer los movimientos; guardamos los movimientos y un registro de la importación (nombre, tamaño y huella del archivo), no el archivo.
- Correos de notificaciones bancarias que decides reenviar a Fino: se procesan para detectar el movimiento; guardamos el movimiento resultante y los remitentes que marcaste como confiables, no el correo completo.

**Datos que se procesan solo en tu teléfono**
- Facturas que escaneas: el texto se lee en tu dispositivo; la imagen no sale de tu teléfono.
- Dictado por voz: lo convierte en texto el servicio de reconocimiento de voz de tu teléfono (Apple o Google); Fino recibe solo el texto.

**Datos del dispositivo y de seguridad**
- Token de notificaciones push, nombre del dispositivo y versión de la app.
- Sesiones activas y registro de actividad de tu cuenta (inicios de sesión, cambios importantes), con la dirección IP guardada de forma cifrada (hash) y el tipo de navegador o app.

**Datos de uso (solo si lo aceptas)**
- Eventos anónimos sobre cómo se usa la app (pantallas abiertas, funciones usadas, errores), para mejorarla. Nunca incluyen montos, comercios, descripciones, saldos, últimos dígitos ni otros datos financieros. Puedes desactivarlo cuando quieras en Perfil.

No tratamos datos sensibles (salud, origen étnico, religión, etc.) ni datos de menores de edad.

## 4. Para qué los usamos y con qué base legal

- **Prestarte el servicio** (crear tu cuenta, importar y categorizar movimientos, calcular saldos, presupuestos, tarjetas, insights y notificaciones): ejecución del contrato que aceptas con los Términos (LOPDP art. 7).
- **Seguridad de tu cuenta** (sesiones, bloqueo por intentos fallidos, registro de actividad, prevención de fraude): interés legítimo y cumplimiento de la obligación de seguridad (arts. 7 y 37).
- **Mejorar la app con datos de uso anónimos:** tu consentimiento, que puedes retirar en cualquier momento sin afectar el resto del servicio (arts. 7 y 8).
- **Notificaciones push:** el permiso que das en tu teléfono; puedes ajustarlas por tipo o apagarlas.
- **Cumplir obligaciones legales** y atender requerimientos de autoridades competentes.

No vendemos tus datos ni los usamos para publicidad de terceros.

## 5. Decisiones automatizadas

Fino categoriza movimientos, detecta duplicados y transferencias entre tus cuentas, y genera insights de forma automática. Estas decisiones solo organizan tu información: no producen efectos jurídicos ni te afectan de forma significativa (LOPDP art. 20). Siempre puedes corregirlas, y tus correcciones mandan sobre las automáticas.

## 6. Con quién compartimos datos

Solo con proveedores que nos ayudan a prestar el servicio (encargados del tratamiento), bajo contrato y únicamente para ese fin:

- **Servidores y base de datos:** {{HOSTING}}.
- **Notificaciones push:** Expo (Estados Unidos), que entrega la notificación a Apple o Google. Si activas "ocultar montos", las notificaciones no llevan montos ni comercios.
- **Datos de uso, solo si lo aceptas:** PostHog (Estados Unidos).
- **Apple y Google:** distribución de la app, notificaciones y reconocimiento de voz del teléfono, según sus propias políticas.

Cuando un proveedor está fuera del Ecuador, la transferencia internacional se hace con las garantías que exige la LOPDP y la norma de la Superintendencia de Protección de Datos Personales (resolución SPDP-SPD-2026-0004-R), como cláusulas contractuales tipo. También compartiremos datos si una autoridad competente lo ordena conforme a la ley.

## 7. Cuánto tiempo los guardamos

- Mientras tu cuenta esté activa.
- Si pides eliminar tu cuenta, se borra de forma definitiva a los {{GRACIA}} días (puedes arrepentirte iniciando sesión antes). Las copias de seguridad se sobrescriben en su ciclo normal.
- Puedes borrar todos tus movimientos o una cuenta en cualquier momento desde Perfil.
- Solo conservaremos algún dato más tiempo si una ley lo exige.

## 8. Cómo protegemos tus datos

Conexiones cifradas (HTTPS), contraseñas con hash, credenciales guardadas en el almacenamiento seguro de tu teléfono, separación estricta de los datos de cada usuario, acceso restringido a los servidores y registro de actividad. Si ocurre una vulneración de seguridad que afecte tus datos, la notificaremos a la Superintendencia de Protección de Datos Personales en máximo 5 días y, si implica un riesgo para ti, te avisaremos en máximo 3 días (LOPDP arts. 43 y 46).

## 9. Tus derechos

Tienes derecho a acceder, rectificar, actualizar, eliminar y portar tus datos, a oponerte o pedir la suspensión de su tratamiento, a no ser objeto de decisiones basadas solo en tratamientos automatizados y a retirar tu consentimiento (LOPDP arts. 12 a 21).

- **Desde la app:** Perfil → Privacidad (exportar tus datos en un archivo, borrar movimientos, eliminar tu cuenta y ajustar los datos de uso).
- **Por correo:** escribe a {{CORREO}} desde el correo de tu cuenta. Respondemos en máximo 15 días.
- Si no estás conforme con la respuesta, puedes acudir a la Superintendencia de Protección de Datos Personales.

## 10. Edad mínima

Fino es solo para personas de 18 años o más. Si detectamos una cuenta de un menor de edad, la eliminaremos.

## 11. Cambios a esta política

Si cambiamos esta política de forma relevante, te lo avisaremos en la app y te pediremos aceptarla de nuevo antes de seguir usándola. La versión vigente siempre está disponible en Perfil.
""";

    public const string Terms = """
# Términos y condiciones de Fino

Versión {{VERSION}}

Estos términos regulan el uso de la app Fino. Al crear tu cuenta y marcar la casilla de aceptación, celebras un contrato electrónico válido conforme a la Ley de Comercio Electrónico, Firmas y Mensajes de Datos. Si no estás de acuerdo, no uses Fino.

## 1. Quién presta el servicio

{{RESPONSABLE}}, con cédula / RUC {{IDENTIFICACION}}, domiciliado en {{DIRECCION}}. Contacto, soporte y reclamos: {{CORREO}}.

## 2. Qué es y qué no es Fino

- Fino organiza la información financiera que tú cargas (estados de cuenta, movimientos registrados a mano, notificaciones bancarias reenviadas) para mostrarte saldos estimados, categorías, presupuestos, tarjetas e insights.
- Fino **no** es un banco, cooperativa, billetera ni medio de pago: no recibe, guarda ni mueve dinero.
- Fino **no** presta asesoría financiera ni administra tus recursos. Los insights, alertas y estimaciones son información general calculada con tus propios datos, no recomendaciones profesionales sobre productos financieros o inversiones. Las decisiones sobre tu dinero son tuyas.
- Los saldos y cifras calculados son **estimados**: dependen de lo que importes o registres y pueden no coincidir con los de tu banco. Para cifras oficiales, consulta siempre a tu entidad financiera.

## 3. Requisitos

- Tener 18 años o más y capacidad legal para contratar.
- Dar datos verdaderos al registrarte y mantener segura tu contraseña. Eres responsable de lo que se haga desde tu cuenta; avísanos de inmediato si sospechas un acceso indebido.
- Cargar solo información de cuentas propias o que tengas derecho a usar.

## 4. Precio

Fino es gratuito por ahora. Si en el futuro ofrecemos funciones de pago, te informaremos el precio en dólares con impuestos incluidos y las condiciones antes de cobrar cualquier valor; nada se cobrará sin tu aceptación expresa.

## 5. Uso permitido

No puedes usar Fino para actividades ilícitas, intentar acceder a datos de otras personas, vulnerar la seguridad de la app, hacer ingeniería inversa, automatizar el acceso de forma abusiva ni cargar contenido malicioso. Podemos suspender o cerrar una cuenta que incumpla estos términos, avisándote el motivo salvo que la ley lo impida.

## 6. Tus datos

Tus datos son tuyos. Cómo los tratamos está en la Política de privacidad, que forma parte de estos términos. Puedes exportarlos o eliminarlos desde Perfil → Privacidad en cualquier momento.

## 7. Disponibilidad y cambios del servicio

Trabajamos para que Fino esté disponible y funcione bien, pero puede haber interrupciones por mantenimiento, fallas técnicas o causas ajenas a nosotros. Podemos mejorar, cambiar o retirar funciones; si retiramos el servicio, te avisaremos con anticipación razonable para que exportes tus datos.

## 8. Responsabilidad

Respondemos por los daños que se deriven de nuestro incumplimiento, dolo o negligencia, conforme a la ley. No respondemos por decisiones financieras que tomes con base en las estimaciones de la app, por errores en la información que cargues o que provenga de tu banco, ni por fallas de servicios de terceros (tu banco, tu proveedor de correo, tu operador o tu teléfono). Nada en estos términos limita los derechos que te reconoce la Ley Orgánica de Defensa del Consumidor.

## 9. Propiedad intelectual

La app, su diseño, marca y código pertenecen a {{RESPONSABLE}}. Te damos una licencia personal, gratuita, no exclusiva e intransferible para usarla mientras tengas tu cuenta.

## 10. Terminación

Puedes dejar de usar Fino y eliminar tu cuenta cuando quieras desde Perfil → Privacidad, sin costo ni penalidad.

## 11. Cambios a estos términos

Si los cambiamos de forma relevante, te avisaremos en la app y te pediremos aceptarlos de nuevo. Si no los aceptas, puedes eliminar tu cuenta y exportar tus datos antes.

## 12. Reclamos, ley aplicable y jurisdicción

Para dudas o reclamos escríbenos a {{CORREO}}; respondemos en máximo 15 días. También puedes acudir a la Defensoría del Pueblo y a los demás mecanismos que prevé la Ley Orgánica de Defensa del Consumidor. Estos términos se rigen por las leyes de la República del Ecuador; cualquier controversia se resolverá ante los jueces competentes del domicilio del usuario consumidor.
""";
}
