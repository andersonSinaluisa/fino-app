# Nexo product tour

Composicion HyperFrames para presentar las funcionalidades de Nexo.

Archivos:

- `BRIEF.md`: intencion y restricciones del video.
- `STORYBOARD.md`: escenas y tiempos.
- `index.html`: composicion 1920x1080, 66 segundos, GSAP.
- `assets/nexo-bgm.wav`: musica de fondo local, sin dependencias externas.
- `tools/generate-bgm.mjs`: generador reproducible de la musica.

Regenerar musica:

```bash
node videos/nexo-product-tour/tools/generate-bgm.mjs
```

Vista previa:

```bash
npx hyperframes preview videos/nexo-product-tour
```

Validacion:

```bash
npx hyperframes check videos/nexo-product-tour
```

Render sugerido:

```bash
npx hyperframes render videos/nexo-product-tour -o videos/nexo-product-tour/dist/nexo-product-tour.mp4 --quality draft --skill=product-launch-video
```

Nota: el render a MP4 requiere `ffmpeg` y `ffprobe` en PATH.
