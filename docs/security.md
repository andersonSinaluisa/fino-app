# Seguridad

Nexo trata todo lo que guarda como datos financieros sensibles, aunque no mueva
dinero.

## Lo que Nexo nunca almacena

* Contraseñas de banca electrónica
* PIN
* OTP
* Números de cuenta o de tarjeta completos (solo los últimos 4 dígitos)
* Contraseñas de correo (el acceso al buzón es siempre OAuth)

No existe scraping de banca web, ni está previsto.

## Autenticación

* **Contraseñas**: PBKDF2-HMAC-SHA512, 210 000 iteraciones, sal por contraseña,
  formato auto-descriptivo `pbkdf2-sha512$iteraciones$sal$hash`. Al iniciar
  sesión, si el hash usa parámetros antiguos se recalcula transparentemente.
* **Access token**: JWT HS256, 15 minutos, con el id de usuario, el nombre y la
  zona horaria. Nada más: ni saldos, ni correo.
* **Refresh token**: valor opaco de 48 bytes. Se guarda **hasheado** (SHA-256) y
  es de un solo uso.
* **Rotación con detección de reuso**: al canjear un refresh se emite otro y el
  anterior queda revocado. Si alguien presenta un token ya rotado, se revoca
  **toda la familia** del usuario y se registra `auth.token_reuse_detected`. Es
  el mecanismo estándar contra un refresh token robado.
* **Almacenamiento en el móvil**: `expo-secure-store` (Keychain / Keystore).
  Nunca `AsyncStorage` en claro.

## Autorización y aislamiento

Dos capas, deliberadamente redundantes:

1. Todo entidad `IUserOwned` recibe un **query filter global** por usuario en el
   `DbContext`, aplicado por reflexión: una entidad nueva no puede olvidarlo.
2. Todos los casos de uso llevan además su `WHERE UserId = ...` explícito.

Los tests de integración comprueban que un usuario recibe `404` (no `403`: no
confirmamos la existencia del recurso ajeno) al pedir la cuenta, el movimiento o
la importación de otro.

## Archivos subidos

| Control | Valor |
| --- | --- |
| Tamaño máximo | 10 MB (validado en el endpoint y al leer el stream) |
| Extensiones | `.csv`, `.xlsx`, `.txt` |
| Content-Type | Lista blanca |
| Filas máximas | 20 000 |
| Contenido | Se lee como texto/ZIP; nunca se ejecuta ni se deserializa como objeto |

El archivo se procesa en memoria y no se persiste: solo quedan las filas
interpretadas y su hash SHA-256, que sirve para avisar de una subida repetida.

## Rate limiting

| Política | Límite |
| --- | --- |
| `auth` (login, registro, refresh) | 10 / minuto por IP o usuario |
| `uploads` (importaciones, correo entrante) | 20 / 5 minutos |
| Global | 300 / minuto |

La partición es por usuario autenticado cuando lo hay y por IP cuando no, y el
middleware corre **después** de la autenticación para que esa distinción sea real.
Detrás de un proxy hace falta que `X-Forwarded-For` llegue: la app confía en el
salto inmediato (`UseForwardedHeaders` con las listas por defecto limpiadas).

`Nexo:RateLimiting:Enabled=false` desactiva solo el middleware —las políticas
siguen registradas— y existe para que una suite automatizada pueda registrar
decenas de usuarios sin chocar contra el presupuesto anti-fuerza-bruta. **Nunca
se desactiva en producción.**

### Bloqueo de cuenta

El rate limiting de `/auth/login` es por IP/usuario y se reinicia cada minuto,
así que un atacante paciente contra **una sola cuenta** no queda realmente
detenido, solo enlentecido. Además del rate limiting, cada cuenta lleva su
propio contador de intentos fallidos consecutivos
(`Nexo:Auth:MaxFailedLoginAttempts`, 5 por defecto): al llegar al límite la
cuenta queda bloqueada por `Nexo:Auth:LockoutMinutes` (15 por defecto), y
durante ese tiempo **ni siquiera la contraseña correcta funciona**. La
respuesta y el registro de auditoría son indistinguibles de una contraseña
incorrecta normal (`user.login_blocked` en vez de `user.login_failed`, pero
con el mismo mensaje al cliente) para no confirmar por otra vía que la cuenta
existe o que está bloqueada.

## Secretos

* `Nexo:Jwt:SigningKey` — mínimo 32 caracteres. **Fuera de Development el
  arranque falla si no está.**
* `Nexo:Secrets:EncryptionKey` — 32 bytes en base64. Cifra los tokens OAuth con
  AES-256-GCM (`v1.nonce.cipher.tag`) y salta el HMAC de las IP del audit log.

Ninguno vive en el repositorio. En desarrollo se usan variables de entorno o
`dotnet user-secrets`; en producción, el gestor de secretos del proveedor.

## Registros

* Logging estructurado en JSON con `CorrelationId` por petición
  (`X-Correlation-Id`, devuelto en la respuesta).
* `EnableSensitiveDataLogging(false)`: EF nunca vuelca valores de parámetros.
* No se registran tokens, montos, descripciones ni direcciones de correo.
* El audit log guarda acciones (`user.logged_in`, `import.confirmed`,
  `privacy.data_exported`…) con la IP **hasheada con HMAC**, nunca en claro.
* Los errores 500 devuelven un mensaje genérico más el `correlationId`; el
  detalle solo va al log del servidor.

## Cabeceras y transporte

TLS obligatorio fuera de Development (HSTS + redirección). Respuestas con
`X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`,
`X-Frame-Options: DENY`, `Content-Security-Policy: default-src 'none'` y
`Permissions-Policy` sin geolocalización/cámara/micrófono. La API es JSON puro
(no sirve HTML), así que estas dos últimas son defensa en profundidad más que
una mitigación de algo explotable hoy.

## CORS

La política `mobile` solo abre orígenes listados en
`Nexo:Cors:AllowedOrigins`. Si esa lista está vacía: en Development se acepta
cualquier origen (Expo Go no tiene un origen de navegador fijo que fijar); en
cualquier otro entorno se cierra en falso (ningún origen permitido) en vez de
caer de vuelta a "cualquier origen" — el mismo criterio de fallo cerrado que
ya se usa con `Nexo:Jwt:SigningKey`. Esto no afecta a la app móvil: CORS es un
mecanismo que solo aplican los navegadores, y Expo/React Native nunca pasa
por él.

---

## Threat model (STRIDE resumido)

| Amenaza | Escenario | Mitigación | Estado |
| --- | --- | --- | --- |
| **S**poofing | Correo falso que aparenta ser del banco | Lista blanca de dominios **+ exigencia de SPF/DKIM/DMARC**; el display name nunca se consulta | Implementado |
| **S**poofing | Robo de refresh token | Rotación de un solo uso + revocación de familia al detectar reuso | Implementado |
| **T**ampering | Alteración de un secreto cifrado | AES-GCM autenticado: un valor manipulado falla al descifrar | Implementado |
| **T**ampering | Webhook de correo forjado | HMAC sobre el cuerpo completo de la petición (no solo el `MessageId`, para que no se pueda reproducir con otro `userId`), comparado en tiempo constante | Implementado |
| **R**epudiation | "Yo no borré mis movimientos" | Audit log append-only con acción, recurso e IP hasheada | Implementado |
| **I**nformation disclosure | Un usuario lee el dinero de otro | Query filter global + predicado explícito + tests de autorización | Implementado |
| **I**nformation disclosure | Fuga por logs | Sin datos sensibles en logs; sensitive data logging apagado | Implementado |
| **I**nformation disclosure | Push visible en pantalla bloqueada | Preferencia por dispositivo: sin preview, el push no lleva montos | Implementado |
| **D**enial of service | Archivo enorme o con millones de filas | Límite de 10 MB y 20 000 filas, lectura con tope | Implementado |
| **D**enial of service | Fuerza bruta de login | Rate limiting por IP/usuario **+ bloqueo temporal por cuenta tras N intentos fallidos** | Implementado |
| **E**levation of privilege | Endpoint sin autorizar | Todos los grupos son `RequireAuthorization()`; solo login/registro/refresh e ingesta firmada son anónimos | Implementado |
| **I**nformation disclosure | Enumeración de cuentas en el registro | El registro responde igual exista o no el correo | Implementado |
| **S**poofing | CORS mal configurado en producción (olvidar `AllowedOrigins`) | Fallo cerrado: sin orígenes configurados, ningún origen se acepta fuera de Development | Implementado |
| **T**ampering | Base de datos comprometida | Cifrado en reposo del proveedor | **Pendiente de despliegue** |
| **S**poofing | Robo del dispositivo con sesión abierta | Biometría al abrir la app | **Pendiente (fase 2)** |
| **I**nformation disclosure | Certificate pinning en el móvil | No implementado | **Pendiente (fase 2)** |

## Privacidad y minimización

* Del correo se extraen únicamente los campos del movimiento: **el cuerpo
  original nunca se persiste**.
* Desconectar un buzón borra el `SecretReference` y el cursor de sincronización.
* Exportación completa en JSON desde el perfil: cuentas, movimientos,
  importaciones, buzones conectados, categorías y reglas propias, insights,
  notificaciones y las sesiones activas (sin el hash del refresh token).
* Borrado: movimientos, una cuenta financiera, o la cuenta Nexo entera. Este
  último revoca sesiones y buzones de inmediato y completa el borrado tras el
  período de gracia (7 días por defecto), mediante cascada desde `users`.
* **El período de gracia es reversible de verdad**: como revocar sesiones es
  inmediato, la única puerta que queda abierta durante los 7 días es un inicio
  de sesión nuevo con correo y contraseña -- por eso `AuthService.LoginAsync`
  cancela la eliminación pendiente automáticamente en cuanto verifica la
  contraseña de una cuenta en ese estado, en vez de rechazar el login.
* **El audit log no seguí la cascada**: `AuditLogEntry` no tiene FK a `users`
  (algunas entradas, como un login fallido contra un correo que nunca existió,
  no tienen usuario al cual asociarse). Decisión explícita: al completarse el
  borrado, sus entradas se conservan pero se desasocian (`UserId = null`) en
  vez de borrarse -- la señal de seguridad sigue siendo válida después de que
  la cuenta ya no existe, pero deja de poder rastrearse hasta esa persona.
* Eliminar movimientos o una cuenta financiera nunca deja un lado de una
  transferencia interna confirmada (Entregable 13) apuntando a una fila que ya
  no existe: el lado que sobrevive se "desmarca" automáticamente.
