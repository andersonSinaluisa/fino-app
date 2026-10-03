# Cumplimiento legal (Ecuador)

Resumen de lo que la app implementa y de lo que falta hacer fuera del código.
No reemplaza la revisión de un abogado.

## En el código

| Requisito | Dónde |
| --- | --- |
| Política de privacidad y Términos versionados (LOPDP art. 12; Ley de Comercio Electrónico) | `Nexo.Application/Legal/LegalTexts.cs`, servidos en `GET /api/v1/legal/{privacidad|terminos}` y como página pública en `/legal/privacidad` y `/legal/terminos` (URL para App Store / Google Play). |
| Datos del responsable configurables | `Nexo:Legal` (`Nexo__Legal__*` en `.env.production`). Sin ellos el documento muestra "(pendiente de completar)". |
| 18+ y aceptación explícita al registrarse (LOPDP arts. 8 y 21) | `POST /auth/register` exige `acceptedTerms` y `confirmedAdult`; casilla sin marcar en `app/(auth)/register.tsx`. |
| Prueba de la aceptación (responsabilidad demostrada) | Tabla `user_consents`: una fila por decisión, con versión y fecha; se incluye en la exportación de datos. |
| Volver a aceptar tras un cambio | Subir `TermsVersion`/`PrivacyVersion`; el layout de pestañas lleva a `app/legal/aceptar.tsx`. Las cuentas anteriores a este flujo también pasan por ahí una vez. |
| Datos de uso solo con consentimiento | `analytics` con `requireExplicitConsent: true`; casilla opcional al registrarse y al aceptar; interruptor en Perfil → Privacidad (`PUT /legal/analytics`). |
| Acceso y portabilidad (arts. 13 y 17) | Perfil → Exportar mis datos: enlace firmado de 5 minutos (`POST /privacy/export-link`). |
| Eliminación (art. 15) | Perfil → Eliminar movimientos / Eliminar cuenta (gracia de 7 días). |
| Canal de reclamos | Perfil → Ayuda y reclamos (`mailto:` al `ContactEmail`). |
| No es asesoría financiera (Ley Fintech, JPRF-F-2025-0155) | Términos §2 y Perfil → Acerca de. |

## Fuera del código (pendiente)

- Completar `Nexo__Legal__ControllerName`, `ControllerId`, `ControllerAddress` y `HostingProvider` en producción.
- Firmar cláusulas contractuales tipo con los proveedores fuera del Ecuador (servidores si aplica, Expo, PostHog) y registrar las transferencias (resolución SPDP-SPD-2026-0004-R).
- Inscribir la base de datos en el Registro Nacional de Protección de Datos (LOPDP art. 51).
- Tener por escrito el plan de respuesta a vulneraciones (5 días a la Superintendencia, 3 días a las personas afectadas).
- Poner la URL `/legal/privacidad` en App Store Connect y Google Play, y llenar App Privacy / Data safety de forma coherente con la política.
- Si en el futuro se cobra algo: RUC, facturación electrónica del SRI y política de reembolsos.
