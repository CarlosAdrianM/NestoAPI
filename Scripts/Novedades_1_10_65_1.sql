/*
    Novedades de Nesto 1.10.65.1 (19/11/2026, revisión): servicio técnico en las notas (NestoAPI#881).
    ANTES de publicar la API: Scripts/Issue881_RepartoFinRutaYTiempos.sql (sa; idempotente; sin él no hay servicio técnico ni
    recogida de tiempos, pero la hora prevista de fin de ruta funciona igual).
    Orden: script → API → Nesto (misma versión 1.10.65, sube solo la última cifra) → Ariadna 2.40 → Novedades_Ariadna_2_40.sql y este.
    Se omite a propósito: fin previsto de ruta (va en las Novedades de Ariadna), aviso de parada nueva, tiempos reales y modelo en sombra.
    Ejecutar contra NV como sa. Idempotente.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @version VARCHAR(23) = '1.10.65.1';
DECLARE @fecha DATE = '2026-11-19';

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, 'Nuevo', N'Notas de entrega y de recogida: servicio técnico',
       N'Al crear una nota de entrega o de recogida (y al confirmar una recogida solicitada) puede indicar que lleva servicio técnico: elija de la lista lo que hay que hacer o escriba uno nuevo, que queda guardado para la próxima vez. Si sabe cuánto va a tardar, ponga los minutos; si no, se cuentan 60. El repartidor lo ve en la parada y ese tiempo cuenta en la hora prevista de fin de su ruta.',
       'Nesto', 1, 'sa', N'Almacén,Administración,Dirección'
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = N'Notas de entrega y de recogida: servicio técnico');
SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;
