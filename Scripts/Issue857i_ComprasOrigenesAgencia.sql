/*
    NestoAPI#857i (Carlos 25/11/26, caso DB Schenker ESVIC262013788): la DELEGACIÓN DE ORIGEN de la agencia → proveedor, APRENDIDA de las
    asignaciones.

    Un aviso de DB Schenker («El remitente [en blanco] ha generado un nuevo envío para usted con número ESVIC262013788») no trae remitente,
    pedido ni referencia: solo el código. Lo único aprovechable es la delegación de salida, que va en el propio código (ES + VIC = Vic +
    9 cifras). Como el proveedor no tiene domicilio allí (sale de otro almacén), no se puede deducir: se aprende.

    Crea dbo.ComprasOrigenesAgencia: una fila por agencia + origen + empresa + proveedor, con cuántas veces se ha visto (Veces), la última
    (UltimaVez) y quién la apuntó la última vez (Usuario; «(automático)» = el job). La API la rellena cuando un envío con origen queda
    aplicado a un pedido por una PERSONA (POST api/Compras/AgenciasEntrada/Correos/{id}/Asignar) o, en el job, por un criterio fuerte
    (nuestro número de pedido, el PDF del aviso o las líneas del aviso). Nunca de una asignación hecha por esta misma regla. La usa solo
    cuando ningún otro criterio da proveedor (job y Reanalizar; el histórico no), con lo visto en los últimos 12 meses.
    Ver y borrar: GET/DELETE api/Compras/AgenciasEntrada/OrigenesAgencia[/{id}] (rastro del borrado en Modificaciones).

    ORDEN: después de Issue857_ComprasCorreosSeguimiento.sql y de Issue857d (si no existe dbo.ComprasCorreosSeguimiento, no hace nada y
    lo dice). Puede ir ANTES o DESPUÉS de publicar la API: sin la tabla, la API sigue exactamente como antes (ni aprende ni la usa, y los
    dos endpoints nuevos dan 503); lo vuelve a comprobar cada 10 minutos, sin reiniciar.

    Lanzar como sa en NV. Idempotente: se puede relanzar. Permisos: la tabla nueva necesita GRANT a [NUEVAVISION\RDS2016$] (como
    ComprasRemitentesPaquete); Modificaciones ya la escribe la API.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NULL
    RAISERROR(N'No existe dbo.ComprasCorreosSeguimiento: lance antes Scripts/Issue857_ComprasCorreosSeguimiento.sql. No se ha cambiado nada.', 16, 1);
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.ComprasOrigenesAgencia', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ComprasOrigenesAgencia (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ComprasOrigenesAgencia PRIMARY KEY CLUSTERED,
        Agencia nvarchar(50) NOT NULL,          -- el nombre de la agencia en la API («DB Schenker»)
        Origen nvarchar(10) NOT NULL,           -- la clave corta que saca la clase de la agencia del código («VIC»)
        Empresa char(3) NOT NULL,
        Proveedor nvarchar(10) NOT NULL,
        Veces int NOT NULL CONSTRAINT DF_ComprasOrigenesAgencia_Veces DEFAULT (1),
        UltimaVez datetime NOT NULL CONSTRAINT DF_ComprasOrigenesAgencia_UltimaVez DEFAULT (GETDATE()),
        Usuario nvarchar(50) NOT NULL
    );
    CREATE UNIQUE INDEX UX_ComprasOrigenesAgencia_Clave ON dbo.ComprasOrigenesAgencia (Agencia, Origen, Empresa, Proveedor);
END
GO
IF OBJECT_ID(N'dbo.ComprasOrigenesAgencia', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ComprasOrigenesAgencia TO [NUEVAVISION\RDS2016$];
GO
-- Comprobación
SELECT CASE WHEN OBJECT_ID(N'dbo.ComprasOrigenesAgencia', N'U') IS NOT NULL
            THEN N'OK: NestoAPI#857i (tabla ComprasOrigenesAgencia)'
            ELSE N'FALTA algo (¿existe dbo.ComprasCorreosSeguimiento?)' END AS Resultado;
GO
