# Agregar un proveedor

Un "proveedor" es un banco o una billetera. El objetivo del diseño es que
agregar uno sea aburrido.

## 1. Registrarlo en el catálogo

En `ReferenceDataSeeder.SeedProvidersAsync`:

```csharp
Provider.Create(
    "BANECUADOR", "BanEcuador", "BanEcuador", ProviderKind.Bank,
    [ConnectionMode.ManualImport],      // solo lo que exista de verdad
    now, "BANECUADOR_V1", "#0B6E4F", "banecuador", 8),
```

**`SupportedModes` es un compromiso, no una aspiración.** La app muestra
exactamente lo que hay aquí, y abrir una cuenta en un modo no soportado lanza
`unsupported_connection_mode`.

## 2. Parser de estado de cuenta

Casi siempre basta con declarar los sinónimos de las columnas:

```csharp
public sealed class BanEcuadorStatementParser : HeaderMappedStatementParser
{
    public override string ParserCode => "BANECUADOR_V1";
    public override string? ProviderCode => "BANECUADOR";
    public override int Priority => 10;   // antes que el genérico (1000)

    protected override IReadOnlyList<string> FileSignatures => ["BANECUADOR"];
    protected override IReadOnlyList<string> DateHeaders => ["FECHA", "FECHA PROCESO"];
    protected override IReadOnlyList<string> DescriptionHeaders => ["DETALLE", "CONCEPTO"];
    protected override IReadOnlyList<string> DebitHeaders => ["DEBITO", "CARGO"];
    protected override IReadOnlyList<string> CreditHeaders => ["CREDITO", "ABONO"];
}
```

La clase base se encarga de encontrar la fila de encabezados, mapear columnas,
leer montos en cualquier formato, interpretar fechas en la zona del usuario,
extraer la máscara de cuenta y reportar filas inválidas sin abortar el archivo.

Si el banco invierte el signo de la columna de débito, sobreescribe
`ExpensesArePositiveInDebitColumn`. Si el formato es tan distinto que no encaja,
implementa `IStatementParser` directamente: el resolver solo pide `CanParse` y
`ParseAsync`.

Regístralo en `DependencyInjection.AddStatementParsers`.

## 3. Parser de correo (opcional)

```csharp
public sealed class BanEcuadorEmailParser : SpanishNotificationEmailParser
{
    public override string ProviderCode => "BANECUADOR";
    public override string ParserCode => "BANECUADOR_EMAIL_V1";
    protected override IReadOnlyList<string> Signatures => ["BANECUADOR"];
}
```

Y el dominio de remitente en `SeedTrustedSendersAsync`. Registra el parser en
`AddBankEmailParsers`.

## 4. Tests

Añade una fixture ficticia en `samples/` y un caso en `StatementParserTests`:
que el resolver elija tu parser, que las direcciones salgan bien y que una fila
inválida no tumbe el archivo.

## 5. Lo que **no** hay que hacer

* No tocar `Transaction`, `ImportService` ni el pipeline de correo. Si un
  proveedor nuevo te obliga a modificarlos, el contrato está mal y hay que
  arreglar el contrato.
* No hacer scraping de banca web.
* No pedir ni almacenar credenciales bancarias.
* No inventar endpoints de una institución. Si no existe API pública o acuerdo,
  el proveedor se queda en `ManualImport`.

## Resolución de parsers

```mermaid
flowchart LR
    F[Archivo] --> T[CSV/XLSX -> tabla]
    T --> R{Resolver por prioridad}
    R -->|Pichincha 10| P1[PichinchaStatementParser]
    R -->|Guayaquil 10| P2[GuayaquilStatementParser]
    R -->|... 10| P3[...]
    R -->|Genérico 1000| PG[GenericStatementParser]
    R -->|nadie lo reclama| X[Import marcado como Failed]
```

El parser genérico acepta cualquier archivo con columnas reconocibles de fecha,
descripción y valor. Es lo que permite que un usuario con un banco no soportado
igual pueda importar hoy.
