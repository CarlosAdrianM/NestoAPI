/*
    NestoAPI#823 (Carlos 05/11/26): una persona, una identidad.

    La misma persona entra en Nesto con el usuario de dominio (NUEVAVISION\Alfredo) y en Ariadna/NestoApp con el de
    Identity, sin dominio (Alfredo). Desde #823 la API resuelve a la persona en un único punto
    (UsuarioAuditoriaHelper.Persona): un nombre SIN dominio que tiene gemelo de dominio (fila «NUEVAVISION\<nombre>» en
    FechasPermitidas o en ParámetrosUsuario de la empresa 1) se graba y se compara como el de dominio. Así los SPs
    (FechasPermitidas: «Fecha no permitida» de Santiago al facturar el pedido 920033 desde Ariadna), los triggers y las
    trazas ven a una sola persona, sin duplicar filas por usuario.

    Este script pasa al usuario de dominio el HISTÓRICO que Ariadna (y NestoApp) grabaron con el nombre sin dominio, para
    que lo de antes y lo de después sea de la misma persona. No hace falta para que la API funcione: las comparaciones de
    Ariadna (furgoneta, soloMios, repartidores, candado de Enviar reposición) ya van sin dominio, y el buzón busca por los
    dos nombres. Se puede lanzar antes o después de publicar la API (mejor después, para no dejar huecos con filas nuevas
    sin dominio grabadas entre medias; si se lanza antes, se vuelve a lanzar después).

    Ejecutar en SSMS contra NV como sa. Idempotente (lo ya pasado no vuelve a coincidir) y en una transacción: si algo
    falla, no se toca nada.

    EL MAPA (no es una lista a mano): cada «NUEVAVISION\<nombre>» de FechasPermitidas (todas las empresas) o de
    ParámetrosUsuario de la empresa 1 → <nombre>. Si está en las dos, manda la caja de FechasPermitidas y, dentro, la de la
    empresa 1 (la misma regla que la API: «NUEVAVISION\santiago» en la 1 y «NUEVAVISION\Santiago» en la 3). Se comparan sin distinguir mayúsculas (la intercalación de la BD) y se graba con la caja de la fila
    (NUEVAVISION\santiago si la fila es así). Quien no tiene gemelo (Pedro, Miguel, RevisionGoogle, los clientes de la
    tienda) no se toca.

    LAS COLUMNAS (donde Ariadna/NestoApp escriben por usuario):
      Inventarios.Usuario, InventariosCuadre.Usuario,
      PreparacionEscaneos.Usuario, PreparacionFotosPacking.Usuario, PreparacionSalidasTerminadas.Usuario,
      PreparacionAnulacionesLecturas.AnuladoPor / UsuarioLecturas,
      EnviosAgencia.Usuario, EnviosAgenciaBultos.Usuario, EnviosAgenciaCargaBultos.Usuario,
      RepartoBultosMovimientos.Usuario, RepartoJornadas.Usuario, RepartoParadasReposicionBultos.Usuario,
      ReposicionesTraspasos.UsuarioCreacion / UsuarioPreparacion / UsuarioRecepcion / UsuarioRecogida,
      PedidosModoServicioOrigen.Usuario,
      NotificacionesBuzon.Usuario (de todas las aplicaciones: Ariadna y NestoApp; la Aplicacion se queda).
    NO se tocan:
      - DispositivosNotificaciones: los dispositivos se registran con el usuario del token (Ariadna y NestoApp lo
        comparan con el que está dentro); la API los busca por los dos nombres.
      - ParámetrosUsuario: los parámetros de verdad están SIN dominio («Alfredo») y la API los lee quitando el dominio.
      - RepartoAutorizacionesReembolso.Repartidor y NotasEntregaCabecera.Repartidor: son el nombre corto de la lista de
        repartidores (sin dominio a propósito).

    Recuento leído en producción el 05/11/26 por la tarde (solo lectura, los pasos 1-3 de este script): 67 gemelos y
    4.701 filas: PreparacionEscaneos 3.769, NotificacionesBuzon 603 (Ariadna y NestoApp: los vendedores de NestoApp con
    usuario de dominio también son gemelos), EnviosAgenciaBultos 93, PreparacionSalidasTerminadas 75, EnviosAgencia 64,
    ReposicionesTraspasos 93 (Creacion 9, Preparacion 35, Recepcion 49), PreparacionFotosPacking 3,
    PreparacionAnulacionesLecturas.UsuarioLecturas 1; Inventarios, la furgoneta (RepartoBultosMovimientos, RepartoJornadas,
    cargas) y PedidosModoServicioOrigen, 0.
*/

USE NV;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- 1) El mapa: nombre sin dominio → usuario de dominio con la caja de su fila.
IF OBJECT_ID('tempdb..#Gemelos') IS NOT NULL DROP TABLE #Gemelos;

;WITH Candidatos AS (
    SELECT RTRIM(Usuario) AS Gemelo, 1 AS Orden, CASE WHEN Empresa = '1' THEN 0 ELSE 1 END AS DeLaUno, Empresa
    FROM dbo.FechasPermitidas
    WHERE Usuario LIKE N'NUEVAVISION\%'
    UNION ALL
    SELECT RTRIM(Usuario), 2, 0, Empresa
    FROM dbo.[ParámetrosUsuario]
    WHERE Empresa = '1' AND Usuario LIKE 'NUEVAVISION\%'
), Numerados AS (
    SELECT Gemelo, SUBSTRING(Gemelo, LEN(N'NUEVAVISION\') + 1, 100) AS SinDominio,
           ROW_NUMBER() OVER (PARTITION BY UPPER(SUBSTRING(Gemelo, LEN(N'NUEVAVISION\') + 1, 100)) ORDER BY Orden, DeLaUno, Empresa) AS N
    FROM Candidatos
)
SELECT CAST(SinDominio AS nvarchar(60)) AS SinDominio, CAST(Gemelo AS nvarchar(60)) AS Gemelo
INTO #Gemelos
FROM Numerados
WHERE N = 1 AND LEN(SinDominio) > 0 AND CHARINDEX(N'\', SinDominio) = 0;

-- Las columnas más cortas son varchar(30): un gemelo más largo no cabría (hoy el más largo tiene 26).
DELETE FROM #Gemelos WHERE LEN(Gemelo) > 30;

SELECT COUNT(*) AS Gemelos FROM #Gemelos;

-- 2) Las columnas a pasar.
IF OBJECT_ID('tempdb..#Columnas') IS NOT NULL DROP TABLE #Columnas;
CREATE TABLE #Columnas (Tabla sysname NOT NULL, Columna sysname NOT NULL, Antes int NULL, Pasadas int NULL, Despues int NULL);
INSERT INTO #Columnas (Tabla, Columna) VALUES
    ('Inventarios', 'Usuario'),
    ('InventariosCuadre', 'Usuario'),
    ('PreparacionEscaneos', 'Usuario'),
    ('PreparacionFotosPacking', 'Usuario'),
    ('PreparacionSalidasTerminadas', 'Usuario'),
    ('PreparacionAnulacionesLecturas', 'AnuladoPor'),
    ('PreparacionAnulacionesLecturas', 'UsuarioLecturas'),
    ('EnviosAgencia', 'Usuario'),
    ('EnviosAgenciaBultos', 'Usuario'),
    ('EnviosAgenciaCargaBultos', 'Usuario'),
    ('RepartoBultosMovimientos', 'Usuario'),
    ('RepartoJornadas', 'Usuario'),
    ('RepartoParadasReposicionBultos', 'Usuario'),
    ('ReposicionesTraspasos', 'UsuarioCreacion'),
    ('ReposicionesTraspasos', 'UsuarioPreparacion'),
    ('ReposicionesTraspasos', 'UsuarioRecepcion'),
    ('ReposicionesTraspasos', 'UsuarioRecogida'),
    ('PedidosModoServicioOrigen', 'Usuario'),
    ('NotificacionesBuzon', 'Usuario');

-- Las que no existan en esta BD (otro entorno) se saltan.
DELETE FROM #Columnas WHERE COL_LENGTH('dbo.' + Tabla, Columna) IS NULL;

DECLARE @tabla sysname, @columna sysname, @sql nvarchar(max), @n int;

-- 3) Recuento ANTES (filas grabadas sin dominio por una persona con gemelo), por columna y por usuario.
DECLARE cAntes CURSOR LOCAL FAST_FORWARD FOR SELECT Tabla, Columna FROM #Columnas;
OPEN cAntes;
FETCH NEXT FROM cAntes INTO @tabla, @columna;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'SELECT @n = COUNT(*) FROM dbo.' + QUOTENAME(@tabla) + N' t JOIN #Gemelos g ON RTRIM(t.' + QUOTENAME(@columna) + N') = g.SinDominio';
    EXEC sp_executesql @sql, N'@n int OUTPUT', @n = @n OUTPUT;
    UPDATE #Columnas SET Antes = @n WHERE Tabla = @tabla AND Columna = @columna;
    FETCH NEXT FROM cAntes INTO @tabla, @columna;
END
CLOSE cAntes;
DEALLOCATE cAntes;

SELECT Tabla, Columna, Antes FROM #Columnas ORDER BY Tabla, Columna;

-- 4) Pasarlas, todas o ninguna.
BEGIN TRANSACTION;

DECLARE cPasar CURSOR LOCAL FAST_FORWARD FOR SELECT Tabla, Columna FROM #Columnas WHERE Antes > 0;
OPEN cPasar;
FETCH NEXT FROM cPasar INTO @tabla, @columna;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'UPDATE t SET ' + QUOTENAME(@columna) + N' = g.Gemelo FROM dbo.' + QUOTENAME(@tabla) + N' t ' +
               N'JOIN #Gemelos g ON RTRIM(t.' + QUOTENAME(@columna) + N') = g.SinDominio; SET @n = @@ROWCOUNT;';
    EXEC sp_executesql @sql, N'@n int OUTPUT', @n = @n OUTPUT;
    UPDATE #Columnas SET Pasadas = @n WHERE Tabla = @tabla AND Columna = @columna;
    FETCH NEXT FROM cPasar INTO @tabla, @columna;
END
CLOSE cPasar;
DEALLOCATE cPasar;

-- 5) Recuento DESPUÉS: tiene que dar 0 en todas.
DECLARE cDespues CURSOR LOCAL FAST_FORWARD FOR SELECT Tabla, Columna FROM #Columnas;
OPEN cDespues;
FETCH NEXT FROM cDespues INTO @tabla, @columna;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'SELECT @n = COUNT(*) FROM dbo.' + QUOTENAME(@tabla) + N' t JOIN #Gemelos g ON RTRIM(t.' + QUOTENAME(@columna) + N') = g.SinDominio';
    EXEC sp_executesql @sql, N'@n int OUTPUT', @n = @n OUTPUT;
    UPDATE #Columnas SET Despues = @n WHERE Tabla = @tabla AND Columna = @columna;
    FETCH NEXT FROM cDespues INTO @tabla, @columna;
END
CLOSE cDespues;
DEALLOCATE cDespues;

IF EXISTS (SELECT 1 FROM #Columnas WHERE ISNULL(Despues, 0) <> 0)
BEGIN
    SELECT Tabla, Columna, Antes, Pasadas, Despues FROM #Columnas WHERE ISNULL(Despues, 0) <> 0;
    ROLLBACK TRANSACTION;
    RAISERROR(N'NestoAPI#823: quedan filas sin pasar; no se ha tocado nada.', 16, 1);
END
ELSE
BEGIN
    COMMIT TRANSACTION;
    SELECT Tabla, Columna, Antes, ISNULL(Pasadas, 0) AS Pasadas, Despues FROM #Columnas ORDER BY Tabla, Columna;
    SELECT SUM(ISNULL(Pasadas, 0)) AS TotalPasadas FROM #Columnas;
END
GO
