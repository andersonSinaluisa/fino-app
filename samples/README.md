# Archivos de ejemplo

Todos los datos aquí son **ficticios**. No hay información bancaria real de
ninguna persona ni institución.

| Archivo | Para qué sirve |
| --- | --- |
| `pichincha-marzo-2026.csv` | Importación feliz con columnas débito/crédito y referencia. |
| `guayaquil-marzo-2026.csv` | Separador `;` y decimales con coma. |
| `generico-billetera.csv` | Columna única de valor con signo: activa el parser genérico. |
| `archivo-invalido.csv` | Debe fallar sin escribir nada en el historial. |
| `correo-pichincha-compra.txt` | Notificación bancaria de ejemplo para el pipeline de correo. |
| `correo-phishing.txt` | Correo forjado: debe rechazarse en la validación del remitente. |

Para probar la deduplicación: importa `pichincha-marzo-2026.csv`, confírmalo y
vuelve a importarlo. La segunda vez el preview debe mostrar 0 movimientos nuevos
y 12 duplicados.
