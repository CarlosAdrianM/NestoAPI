/*
    NestoAPI#779 (sigue a #777): el calendario de festivos en una tabla en vez de codificado por año en GestorFestivos.cs, para
    que un festivo nuevo (o un año entero) no necesite un deploy. El olvido del 02/11/2026 descuadró remesas, fechas de
    entrega, picking de rutas internas, avisos y recordatorios.

    Una fila = esa fecha es festivo de ese tipo. Tipo (el nombre del enum TipoFestivo):
      - Nacional y Autonómica: cuentan en todas las delegaciones y sin delegación.
      - Empresa (Nochebuena, Nochevieja): Algete, Alcobendas y Reina (no «sin delegación»).
      - Alcobendas: solo la delegación ALC. Madrid: solo REI.
      - Algete: se guarda, pero hoy GestorFestivos.EsFestivo NO lo aplica a ALG (Algete trabaja sus fiestas locales).
    Mismo comportamiento que la lista del código (GestorFestivos.EsFestivo no cambia).

    Fuera del EDMX: la API la lee por SQL (CalendarioFestivos, caché de 10 min que invalidan POST/DELETE api/Festivos). Sin la
    tabla, la API usa la lista del código como hasta ahora; con la tabla pero sin filas de un año, ELMAH (una vez por año) y
    la lista del código de ese año. Escriben POST api/Festivos y DELETE api/Festivos/{fecha}/{tipo} (Administración,
    Dirección, Informática); cada cambio deja su fila en Modificaciones. El día 1 de cada mes (job festivos-sin-cargar) se
    avisa a Dirección si no hay festivos del año que toca (en diciembre, del siguiente).

    Lanzar como sa en NV ANTES (o después: sin la tabla todo sigue igual) de publicar la API. Idempotente: solo inserta las
    fechas que falten; no pisa lo que se haya cambiado a mano.
    Sin índices filtrados ni columnas calculadas indexadas (rompen los SET de las conexiones viejas de Nesto).
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.Festivos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Festivos (
        Fecha date NOT NULL,
        Tipo nvarchar(20) NOT NULL,             -- Nacional, Autonómica, Empresa, Algete, Alcobendas, Madrid
        Descripcion nvarchar(100) NOT NULL,
        Usuario nvarchar(100) NOT NULL,         -- quien lo dio de alta o lo cambió por última vez
        FechaModificacion datetime NOT NULL CONSTRAINT DF_Festivos_FechaModificacion DEFAULT (GETDATE()),
        CONSTRAINT PK_Festivos PRIMARY KEY CLUSTERED (Fecha, Tipo),
        CONSTRAINT CK_Festivos_Tipo CHECK (Tipo IN (N'Nacional', N'Autonómica', N'Empresa', N'Algete', N'Alcobendas', N'Madrid'))
    );
END
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Festivos TO [NUEVAVISION\RDS2016$];
GO

-- 2024-2026: EXACTAMENTE lo que hay hoy en GestorFestivos.cs (lo comprueba GestorFestivosScriptTests). Ojo: el código tiene
-- «Día de Todos los Santos» de 2024 el 11/01/2024 (debería ser el 01/11/2024, que era viernes). Se copia tal cual para no
-- cambiar el comportamiento; es pasado.
DECLARE @Festivos TABLE (Fecha date NOT NULL, Tipo nvarchar(20) NOT NULL, Descripcion nvarchar(100) NOT NULL);
INSERT INTO @Festivos (Fecha, Tipo, Descripcion) VALUES
-- BEGIN CODIGO
-- 2024
('20240101', N'Nacional', N'Año nuevo'),
('20240106', N'Nacional', N'Epifanía del Señor'),
('20240328', N'Nacional', N'Jueves Santo'),
('20240329', N'Nacional', N'Viernes Santo'),
('20240501', N'Nacional', N'Día del trabajo'),
('20240502', N'Nacional', N'Día de la Comunidad de Madrid'),
('20240725', N'Nacional', N'Santiago Apostol'),
('20240815', N'Nacional', N'Asunción de la Virgen'),
('20241012', N'Nacional', N'Fiesta Nacional Española'),
('20240111', N'Nacional', N'Día de Todos los Santos'),
('20241206', N'Nacional', N'Día de la Constitución Española'),
('20241225', N'Nacional', N'Navidad'),
('20240515', N'Madrid', N'San Isidro Labrador'),
('20241109', N'Madrid', N'Nuestra Señora de la Almudena'),
('20240124', N'Alcobendas', N'Nuestra Señora de la Paz'),
('20240515', N'Alcobendas', N'San Isidro'),
('20241224', N'Nacional', N'Nochebuena'),
('20241231', N'Nacional', N'Nochevieja'),
-- 2025
('20250101', N'Nacional', N'Año nuevo'),
('20250106', N'Nacional', N'Epifanía del Señor'),
('20250417', N'Nacional', N'Jueves Santo'),
('20250418', N'Nacional', N'Viernes Santo'),
('20250501', N'Nacional', N'Día del trabajo'),
('20250815', N'Nacional', N'Asunción de la Virgen'),
('20251101', N'Nacional', N'Día de Todos los Santos'),
('20251206', N'Nacional', N'Día de la Constitución Española'),
('20251208', N'Nacional', N'Inmaculada Concepción'),
('20251225', N'Nacional', N'Navidad'),
('20250502', N'Autonómica', N'Día de la Comunidad de Madrid'),
('20250725', N'Autonómica', N'Santiago Apostol'),
('20250515', N'Madrid', N'San Isidro Labrador'),
('20251110', N'Madrid', N'Nuestra Señora de la Almudena'),
('20250912', N'Algete', N'Fiestas de Algete (viernes)'),
('20250915', N'Algete', N'Fiestas de Algete (lunes)'),
('20250124', N'Alcobendas', N'Nuestra Señora de la Paz'),
('20250515', N'Alcobendas', N'San Isidro'),
('20251224', N'Empresa', N'Nochebuena'),
('20251231', N'Empresa', N'Nochevieja'),
-- 2026
('20260101', N'Nacional', N'Año nuevo'),
('20260106', N'Nacional', N'Epifanía del Señor'),
('20260403', N'Nacional', N'Viernes Santo'),
('20260501', N'Nacional', N'Día del trabajo'),
('20260815', N'Nacional', N'Asunción de la Virgen'),
('20261012', N'Nacional', N'Fiesta Nacional de España'),
('20261101', N'Nacional', N'Día de Todos los Santos'),
('20261207', N'Nacional', N'Día de la Constitución Española'),
('20261208', N'Nacional', N'Inmaculada Concepción'),
('20261225', N'Nacional', N'Navidad'),
('20260402', N'Autonómica', N'Jueves Santo'),
('20260502', N'Autonómica', N'Día de la Comunidad de Madrid'),
('20261102', N'Autonómica', N'Traslado de Todos los Santos (Comunidad de Madrid)'),
('20260515', N'Madrid', N'San Isidro Labrador'),
('20261110', N'Madrid', N'Nuestra Señora de la Almudena'),
('20260911', N'Algete', N'Fiestas de Algete (viernes)'),
('20260914', N'Algete', N'Fiestas de Algete (lunes)'),
('20260124', N'Alcobendas', N'Nuestra Señora de la Paz'),
('20260515', N'Alcobendas', N'San Isidro'),
('20261224', N'Empresa', N'Nochebuena'),
('20261231', N'Empresa', N'Nochevieja');
-- END CODIGO

INSERT INTO dbo.Festivos (Fecha, Tipo, Descripcion, Usuario)
SELECT f.Fecha, f.Tipo, f.Descripcion, N'Script Issue779'
FROM @Festivos f
WHERE NOT EXISTS (SELECT 1 FROM dbo.Festivos t WHERE t.Fecha = f.Fecha AND t.Tipo = f.Tipo);
PRINT CONCAT(@@ROWCOUNT, N' festivos de 2024-2026 insertados');
GO

/*
    2027 — REVISAR ANTES DE LANZAR (no se ha lanzado nada: descomentar el bloque cuando se haya revisado).
    Fuente: BOE de 13/10/2026, Resolución de 8 de octubre de 2026, de la Dirección General de Trabajo, por la que se
    publica la relación de fiestas laborales para el año 2027, BOE-A-2026-21254, anexo, columna de Madrid:
    https://www.boe.es/diario_boe/txt.php?id=BOE-A-2026-21254
    Coincide con lo que publica la prensa (Time Out, diario.madrid.es) del Decreto 82/2026, de 30 de septiembre, del Consejo
    de Gobierno de la Comunidad de Madrid (BOCM de 01/10/2026; el texto del BOCM no se ha podido leer).
    El 2 de mayo (Fiesta de la Comunidad) cae en domingo y no se traslada; el 15 de agosto cae en domingo y la
    Comunidad lo pasa al lunes 16. Clasificación como en 2026: las del Estado, Nacional; San José, Jueves Santo y el lunes
    16/08 (las que la Comunidad elige), Autonómica.
    NO van: las fiestas locales de Madrid (REI), Alcobendas (ALC) y Algete de 2027, ni Nochebuena/Nochevieja (Empresa) de
    2027 (24/12 y 31/12 caen en viernes): eso lo decide Dirección y se da de alta desde Nesto o POST api/Festivos.

INSERT INTO dbo.Festivos (Fecha, Tipo, Descripcion, Usuario)
SELECT f.Fecha, f.Tipo, f.Descripcion, N'Script Issue779'
FROM (VALUES
    ('20270101', N'Nacional', N'Año nuevo'),
    ('20270106', N'Nacional', N'Epifanía del Señor'),
    ('20270326', N'Nacional', N'Viernes Santo'),
    ('20270501', N'Nacional', N'Fiesta del Trabajo'),
    ('20271012', N'Nacional', N'Fiesta Nacional de España'),
    ('20271101', N'Nacional', N'Todos los Santos'),
    ('20271206', N'Nacional', N'Día de la Constitución Española'),
    ('20271208', N'Nacional', N'Inmaculada Concepción'),
    ('20271225', N'Nacional', N'Natividad del Señor'),
    ('20270319', N'Autonómica', N'San José'),
    ('20270325', N'Autonómica', N'Jueves Santo'),
    ('20270816', N'Autonómica', N'Lunes siguiente a la Asunción de la Virgen')
) AS f (Fecha, Tipo, Descripcion)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Festivos t WHERE t.Fecha = CAST(f.Fecha AS date) AND t.Tipo = f.Tipo);
PRINT CONCAT(@@ROWCOUNT, N' festivos de 2027 insertados');
*/

-- Comprobación
SELECT YEAR(Fecha) AS Anio, Tipo, COUNT(*) AS Festivos FROM dbo.Festivos GROUP BY YEAR(Fecha), Tipo ORDER BY Anio, Tipo;
SELECT Fecha, Tipo, Descripcion, Usuario, FechaModificacion FROM dbo.Festivos WHERE Fecha >= '20260101' ORDER BY Fecha, Tipo;
