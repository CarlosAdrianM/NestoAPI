-- OneShot 17/11/26 (Novedades 1308, Enrique): pide UNA pasada de reenvío a la tienda de todos los productos activos con stock.
-- La hace el job de Hangfire «sincronizar-stocks-nocturno» (2:00) con la API ya publicada (SincronizacionStocksJobsService,
-- parámetro SincronizarStocks.ReenviarConStock): encola en Nesto_sync los productos con stock en ALG/REI/ALC y deja el
-- parámetro a 0. No hace falta tener ningún ordenador encendido. Para repetirlo otro día, volver a lanzar este script.

USE NV;
GO

IF EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'SincronizarStocks.ReenviarConStock')
    UPDATE ParametrosUsuario SET Valor = '1', Usuario2 = SYSTEM_USER, [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'SincronizarStocks.ReenviarConStock';
ELSE
    INSERT INTO ParametrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'SincronizarStocks.ReenviarConStock', '1', SYSTEM_USER, GETDATE());
GO

SELECT Clave, Valor, Usuario2, [Fecha Modificación] FROM ParametrosUsuario
WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'SincronizarStocks.ReenviarConStock';
GO
