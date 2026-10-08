/*
    NestoAPI#577 (corte 3a): ReposicionesTraspasos, una cabecera por traspaso de reposición para saber con qué
    herramienta se hizo, quién y cuándo se rellenó, se preparó y se recibió.

    Decisión de Carlos (08/10/26): «una cabecera por traspaso con la herramienta (Nesto, Ariadna o el job), quién, y cuándo
    se rellena, se prepara y se recibe. Nesto viejo no escribe ahí, así que un traspaso SIN cabecera es de Nesto viejo».

    Clave (decisión de Carlos, 08/10/26): Id propio (IDENTITY) y NumTraspaso NULL hasta que haya número. NO se reservan
    números del contador compartido (ContadoresGlobales.TraspasoAlmacén). Único por Empresa + NumTraspaso cuando lo tiene
    (índice único FILTRADO, WHERE NumTraspaso IS NOT NULL: un único normal solo admitiría un NULL).

    Quién escribe (NestoAPI):
      - POST api/Reposiciones (ServicioPreparacionReposicion.Crear):
          · desde Algete (control de ubicaciones) la reposición nace ya numerada: fila con su NumTraspaso.
          · desde una tienda la reposición queda EN PREPARACIÓN SIN número (PreExtrProducto.NºTraspaso NULL hasta Terminar,
            como en Nesto viejo) y su fila también, con NumTraspaso NULL. Terminar coge el número del contador (como
            siempre) y se lo pone a la fila ABIERTA (NumTraspaso NULL y FechaPreparada NULL) de ese origen y destino. Como
            mucho hay una reposición en preparación por diario de salida, así que como mucho una abierta; si hubiera varias
            (una que terminó Nesto viejo, que no escribe aquí, se queda abierta), la más reciente.
      - Terminar en la tienda (POST api/Reposiciones/EnPreparacion/Terminar) y terminar la recogida en Ariadna (Salidas,
        REPO): UsuarioPreparacion y FechaPreparada.
      - Recibir en Ariadna (Entradas, REPO): UsuarioRecepcion y FechaRecibida.
      - DELETE api/Reposiciones/{n} (anular): se borra la fila.
    Si la tabla no existe todavía, la API sigue funcionando igual que antes (cada escritura comprueba OBJECT_ID).

    Herramienta: 'Nesto', 'Ariadna' o 'Automatico' (el job del corte 3b). Si la petición no la dice, se deduce del usuario:
    con dominio («NUEVAVISION\Paloma», Nesto entra por api/auth/windows-token) = Nesto; sin dominio («Andre», Ariadna entra
    por /oauth/token con el usuario del Identity) = Ariadna.

    FechaCorte: el instante de corte con el que el job rellenó la reposición (día + HoraCierre del calendario). NULL si se
    hizo a mano.

    ORDEN: independiente de los otros dos scripts del corte 3a. Mejor ANTES del deploy (la tabla está en el EDMX; sin ella
    la API funciona pero no apunta nada). Idempotente.

    Ejecutar en SSMS contra NV como sa (crea una tabla: el login nuevavision no tiene ALTER).

    PRECAUCIÓN (#542, #294): el índice filtrado obliga a QUOTED_IDENTIFIER y ANSI_NULLS ON en TODO lo que escriba en la
    tabla. Aquí es seguro: es una tabla nueva que solo escribe NestoAPI con SQL ad hoc desde SqlClient (que conecta con
    las dos opciones ON); ningún trigger ni procedimiento ni job del Agente la toca. Si algún día la escribe un
    procedimiento, que se cree con SET QUOTED_IDENTIFIER ON y SET ANSI_NULLS ON. Este script se lanza desde SSMS (que
    también las tiene ON); si se lanza con sqlcmd, con -I.
*/

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
USE NV;
GO

IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NULL
BEGIN
    CREATE TABLE dbo.ReposicionesTraspasos (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        NumTraspaso int NULL,                           -- = PreExtrProducto/ExtractoProducto.NºTraspaso; NULL mientras la tienda la prepara
        Origen char(3) NOT NULL,                        -- almacén que manda (ALG, REI, ALC)
        Destino char(3) NOT NULL,                       -- almacén que recibe
        Herramienta varchar(20) NOT NULL,               -- 'Nesto', 'Ariadna' o 'Automatico'
        UsuarioCreacion varchar(30) NOT NULL,           -- mismo ancho que PreExtrProducto.Usuario
        FechaCreacion datetime NOT NULL CONSTRAINT DF_ReposicionesTraspasos_FechaCreacion DEFAULT (GETDATE()),
        FechaCorte datetime NULL,                       -- instante de corte del job (NULL si se hizo a mano)
        UsuarioPreparacion varchar(30) NULL,            -- quien terminó la salida (tienda: Terminar; Algete: recogida en Ariadna)
        FechaPreparada datetime NULL,
        UsuarioRecepcion varchar(30) NULL,              -- quien la recibió en el destino (Ariadna, Entradas)
        FechaRecibida datetime NULL,
        CONSTRAINT PK_ReposicionesTraspasos PRIMARY KEY (Id),
        CONSTRAINT CK_ReposicionesTraspasos_Almacenes CHECK (Origen <> Destino),
        CONSTRAINT CK_ReposicionesTraspasos_Herramienta CHECK (Herramienta IN ('Nesto', 'Ariadna', 'Automatico'))
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ReposicionesTraspasos') AND name = 'UX_ReposicionesTraspasos_Traspaso')
    CREATE UNIQUE INDEX UX_ReposicionesTraspasos_Traspaso ON dbo.ReposicionesTraspasos (Empresa, NumTraspaso)
        WHERE NumTraspaso IS NOT NULL;
GO

-- Para encontrar la abierta de un origen al terminar (y, corte 3b, la del job por ruta y corte)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ReposicionesTraspasos') AND name = 'IX_ReposicionesTraspasos_Ruta')
    CREATE INDEX IX_ReposicionesTraspasos_Ruta ON dbo.ReposicionesTraspasos (Empresa, Origen, Destino, FechaCreacion);
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ReposicionesTraspasos TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN: la tabla, vacía hasta la primera reposición creada desde la API
------------------------------------------------------------------------------------------------
SELECT c.name, t.name AS tipo, c.max_length, c.is_nullable
FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.ReposicionesTraspasos')
ORDER BY c.column_id;

SELECT TOP 20 * FROM dbo.ReposicionesTraspasos ORDER BY FechaCreacion DESC;
GO
