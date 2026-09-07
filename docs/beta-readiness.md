# Beta cerrada — checklist de lanzamiento

Entregable 33 del plan de 34. No tengo el texto original en español de este
entregable, solo la etiqueta corta "Beta cerrada". A diferencia de los
entregables técnicos anteriores, este es en su mayoría una actividad de
producto y de personas (reclutar testers, distribuir un build, recolectar
feedback), no de código — así que este documento es un checklist de
lanzamiento y una plantilla de feedback, no una funcionalidad nueva.

## Requisito bloqueante: no hay forma de distribuir un build real todavía

Una beta cerrada necesita instalar la app en el teléfono de alguien que no
sea Anderson. Hoy eso no es posible desde este entorno ni con lo que existe
en el repositorio:

* `eas build` necesita una cuenta de Expo real (`extra.eas.projectId` en
  `app.json` sigue siendo el placeholder de ceros — Entregable 31). Sin eso
  no hay APK ni build de iOS que compartir.
* Sin un build development/preview real, la única alternativa es Expo Go
  apuntando a `npx expo start` en la misma red que el teléfono del tester —
  funciona para un tester que esté físicamente cerca de la máquina de
  desarrollo, pero no escala a "varias personas probando por su cuenta".
* No hay un backend alcanzable desde fuera de `localhost` (Entregable 30:
  staging tampoco existe). Un tester en Expo Go necesita `EXPO_PUBLIC_API_URL`
  apuntando a algo que su teléfono pueda alcanzar — la LAN de Anderson, como
  mínimo, o un túnel (ngrok/Cloudflare Tunnel) sobre el backend local si el
  tester no está en la misma red.

**Esto no se puede resolver escribiendo más código.** Es la misma decisión de
infraestructura ya señalada en el Entregable 30 (staging) y 31 (cuenta EAS):
Anderson decide cuándo y cómo se provisiona.

## Antes de invitar al primer tester externo

- [ ] Todos los hallazgos **Bloqueante** y **Alta** del plan de QA
      (`docs/qa-test-plan.md`) están resueltos.
- [ ] Existe una forma real de que el tester instale o acceda a la app: un
      build de EAS (`preview`), o Expo Go + túnel/LAN si el grupo es muy
      pequeño y cercano.
- [ ] El backend que usarán los testers es alcanzable desde su red y no es
      la base de datos de desarrollo de Anderson (evita que un dato de
      prueba de un tester se mezcle con datos reales de otro, y que un bug
      de un tester le borre trabajo a otro).
- [ ] Cada tester tiene su propia cuenta Nexo — nunca se comparte una cuenta
      entre varias personas (rompería el aislamiento por usuario que el
      resto del proyecto se toma tan en serio).
- [ ] El tester recibió y entendió el aviso de privacidad de abajo, **antes**
      de crear su cuenta, no después.
- [ ] Existe un canal para reportar problemas (ver la plantilla de feedback)
      y alguien revisándolo activamente durante la beta.

## Aviso de privacidad para testers (borrador)

Lo que sigue es un borrador, no un texto legal. Está escrito para ser
honesto sobre lo que la app hace de verdad hoy — cada afirmación está
tomada directamente de `docs/security.md` y del código, no inventada — pero
esto es un producto en fase de beta cerrada, sin auditoría de seguridad
externa ni revisión legal, y Ecuador tiene su propia Ley Orgánica de
Protección de Datos Personales (LOPDP). Si vas a invitar a personas fuera de
tu círculo de confianza inmediato, especialmente con datos financieros
reales (no ficticios), vale la pena que un abogado revise este texto antes
de usarlo — eso no es algo que yo pueda hacer.

> **Estás probando una beta cerrada de Nexo.**
>
> Nexo es un gestor de finanzas personales en desarrollo. Esto es lo que
> necesitas saber antes de usarlo con tus datos:
>
> * Nexo nunca te pide ni guarda tu contraseña de banca electrónica, tu PIN,
>   un código OTP, ni el número completo de tu tarjeta o cuenta (solo los
>   últimos 4 dígitos). No hay ni habrá scraping de tu banca en línea.
> * Si conectas tu correo, solo se extraen los datos del movimiento
>   (monto, fecha, comercio) de la notificación bancaria — el correo
>   original nunca se guarda.
> * Puedes exportar todos tus datos en cualquier momento desde tu perfil.
> * Puedes borrar tus movimientos, una cuenta financiera, o tu cuenta Nexo
>   completa. Borrar la cuenta completa tiene un período de gracia de 7 días
>   en el que puedes arrepentirte simplemente volviendo a iniciar sesión;
>   pasado ese plazo, el borrado es definitivo.
> * Es una beta: puede tener errores, puede perder datos a pesar de las
>   protecciones ya construidas, y las funcionalidades pueden cambiar sin
>   aviso. No lo uses todavía como tu único registro de tus finanzas — llévalo
>   en paralelo con lo que ya usas.
> * Tus datos son tuyos y no se comparten con nadie más que tú.

## Plantilla de feedback / reporte de bugs

Para que cada reporte de un tester sea accionable sin ida y vuelta, pídeles
este formato (o pégalo en un formulario):

```
Qué esperabas que pasara:
Qué pasó en realidad:
Pasos para reproducirlo:
Pantalla / momento donde pasó:
¿Perdiste algún dato o viste datos que no eran tuyos? (sí/no — esto es
  siempre Bloqueante si la respuesta es sí)
Capturas de pantalla, si puedes:
```

Clasifica cada reporte con la misma rúbrica de `docs/qa-test-plan.md`
(Bloqueante / Alta / Media / Baja). Un solo hallazgo Bloqueante de un tester
real pesa más que diez de Media — repite el guion correspondiente del plan
de QA para confirmarlo antes de arreglarlo a ciegas.

## Criterios de salida de la beta (hacia el Entregable 34)

La beta cerrada está lista para la decisión Go/No-Go cuando:

- [ ] Ningún hallazgo Bloqueante o Alta sigue abierto.
- [ ] Al menos un puñado de testers reales completó los flujos centrales
      (crear cuenta, importar un extracto propio, ver su dashboard) sin
      ayuda de Anderson.
- [ ] Nadie reportó ver datos de otra persona ni perder datos propios sin
      poder recuperarlos dentro del período de gracia.
