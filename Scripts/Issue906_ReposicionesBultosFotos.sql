/*
    NestoAPI#906 (Carlos 24 y 26/11/26, «evitará disputas»): una foto por bulto R{traspaso}-{n} al ENVIAR la reposición.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT). Idempotente: se puede lanzar dos veces. Fuera del EDMX (la API la lee y
    escribe con SQL a mano): sin ella, POST/GET api/Reposiciones/{traspaso}/Bultos/{bulto}/Foto responden 503 y el seguimiento y las
    discusiones dicen que ningún bulto tiene foto (todo como hasta ahora). Requiere dbo.ReposicionesBultos (Issue645_ReposicionesBultos.sql).

    Una fila por bulto (Empresa, NumTraspaso, Bulto): la última foto sustituye a la anterior (las anteriores siguen en el blob «bultos»,
    reposiciones/{empresa}/{traspaso}/bulto-{n}-{guid}.jpg). Va aparte de ReposicionesBultos porque esa tabla se rehace al cambiar el
    número de bultos; una foto anterior a la creación del bulto (Fecha < ReposicionesBultos.FechaCreacion) no cuenta.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.ReposicionesBultosFotos') IS NULL
BEGIN
    CREATE TABLE dbo.ReposicionesBultosFotos (
        Empresa char(3) NOT NULL,
        NumTraspaso int NOT NULL,
        Bulto smallint NOT NULL,
        RutaBlob varchar(300) NOT NULL,
        Fecha datetime NOT NULL,
        Usuario varchar(50) NULL,
        Bytes int NULL,

        CONSTRAINT PK_ReposicionesBultosFotos PRIMARY KEY CLUSTERED (Empresa, NumTraspaso, Bulto)
    );
END
GO

GRANT SELECT, INSERT, UPDATE ON dbo.ReposicionesBultosFotos TO [NUEVAVISION\RDS2016$];
GO

SELECT 'ReposicionesBultosFotos' AS Tabla, CASE WHEN OBJECT_ID('dbo.ReposicionesBultosFotos') IS NULL THEN 'FALTA' ELSE 'OK' END AS Estado;
GO
