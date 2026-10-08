/*
    NestoAPI#577 (corte 3a): ReposicionesTraspasos, una cabecera por traspaso de reposición para saber con qué
    herramienta se hizo, quién y cuándo se rellenó, se preparó y se recibió.

    Decisión de Carlos (08/10/26): «una cabecera por traspaso con la herramienta (Nesto, Ariadna o el job), quién, y cuándo
    se rellena, se prepara y se recibe. Nesto viejo no escribe ahí, así que un traspaso SIN cabecera es de Nesto viejo».

    Quién escribe (NestoAPI):
      - POST api/Reposiciones (ServicioPreparacionReposicion.Crear):
          · desde Algete (control de ubicaciones) la reposición nace ya numerada: fila con su NumTraspaso.
          · desde una tienda la reposición queda EN PREPARACIÓN SIN número (PreExtrProducto.NºTraspaso NULL hasta Terminar,
            como en Nesto viejo). Para poder guardar quién y con qué la creó, se RESERVA ya el número del contador
            compartido (ContadoresGlobales.TraspasoAlmacén) y se guarda aquí; Terminar usa ESE número al numerar las líneas.
            Si la reposición se termina desde Nesto viejo (que coge otro número), la reserva se queda sin preparar y sin
            ninguna fila en PreExtrProducto: la siguiente reposición que se cree desde ese origen la reutiliza.
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

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NULL
BEGIN
    CREATE TABLE dbo.ReposicionesTraspasos (
        Empresa char(3) NOT NULL,
        NumTraspaso int NOT NULL,                       -- = PreExtrProducto/ExtractoProducto.NºTraspaso (ContadoresGlobales.TraspasoAlmacén)
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
        CONSTRAINT PK_ReposicionesTraspasos PRIMARY KEY (Empresa, NumTraspaso),
        CONSTRAINT CK_ReposicionesTraspasos_Almacenes CHECK (Origen <> Destino),
        CONSTRAINT CK_ReposicionesTraspasos_Herramienta CHECK (Herramienta IN ('Nesto', 'Ariadna', 'Automatico'))
    );
END
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
