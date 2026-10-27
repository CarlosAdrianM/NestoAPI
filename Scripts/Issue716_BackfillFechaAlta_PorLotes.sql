/*
    Issue716, sección 5 POR LOTES: backfill aproximado de Clientes.FechaAlta.

    Por qué existe este script (27/10/26): la sección 5 de Issue716_DatosParaModelos.sql hace UN solo UPDATE de ~55.000
    fichas. El UPDATE en sí tarda segundos, pero el trigger trgClientesUpd, en su último bloque (el que apunta en Nesto_sync
    y solo corre cuando SYSTEM_USER no es NUEVAVISION\RDS2016$, es decir, cuando lo lanza sa), hace
        IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON ...)
    sobre 55.000 × 55.000 filas sin índice: un bucle anidado de más de 3.000 millones de comparaciones, con la tabla
    Clientes bloqueada entera mientras tanto (más de 10 minutos el 27/10 a las 19:00 y bloqueando a los demás).

    Con lotes de 2.000 fichas cada disparo del trigger compara 2.000 × 2.000 = 4 millones de filas (menos de un segundo),
    y entre lote y lote se sueltan los bloqueos. Idempotente: solo toca FechaAlta IS NULL, y las fichas sin pedidos ni
    rapports se quedan a NULL (no hay de dónde sacarlo). Si el UPDATE grande se mató a medias, este lo completa.

    Ejecutar en SSMS contra NV como sa. Imprime el avance por lote.
*/
SET NOCOUNT ON;
USE NV;

IF COL_LENGTH('dbo.Clientes', 'FechaAlta') IS NULL
BEGIN
    RAISERROR('Falta Clientes.FechaAlta: lanzar antes Issue716_DatosParaModelos.sql (secciones 1-4).', 16, 1);
    RETURN;
END

DECLARE @lote int = 2000, @hechas int = 1, @total int = 0;

WHILE @hechas > 0
BEGIN
    ;WITH Pendientes AS (
        SELECT TOP (@lote) c.Empresa, c.[Nº Cliente] AS Cliente, c.Contacto,
               (SELECT MIN(p.Fecha) FROM dbo.CabPedidoVta p WITH (NOLOCK)
                 WHERE p.Empresa = c.Empresa AND p.[Nº Cliente] = c.[Nº Cliente] AND p.Contacto = c.Contacto) AS PrimerPedido,
               (SELECT MIN(s.Fecha) FROM dbo.SeguimientoCliente s WITH (NOLOCK)
                 WHERE s.Empresa = c.Empresa AND s.Número = c.[Nº Cliente] AND s.Contacto = c.Contacto) AS PrimerRapport
        FROM dbo.Clientes c WITH (NOLOCK)
        WHERE c.FechaAlta IS NULL
          AND (EXISTS (SELECT 1 FROM dbo.CabPedidoVta p WITH (NOLOCK)
                        WHERE p.Empresa = c.Empresa AND p.[Nº Cliente] = c.[Nº Cliente] AND p.Contacto = c.Contacto)
            OR EXISTS (SELECT 1 FROM dbo.SeguimientoCliente s WITH (NOLOCK)
                        WHERE s.Empresa = c.Empresa AND s.Número = c.[Nº Cliente] AND s.Contacto = c.Contacto))
        ORDER BY c.Empresa, c.[Nº Cliente], c.Contacto
    )
    UPDATE c
       SET FechaAlta = CASE WHEN p.PrimerPedido IS NULL THEN p.PrimerRapport
                            WHEN p.PrimerRapport IS NULL THEN p.PrimerPedido
                            WHEN p.PrimerRapport < p.PrimerPedido THEN p.PrimerRapport
                            ELSE p.PrimerPedido END,
           UsuarioAlta = N'Backfill Issue716',
           AplicacionAlta = 'Backfill'
      FROM dbo.Clientes c
      INNER JOIN Pendientes p ON p.Empresa = c.Empresa AND p.Cliente = c.[Nº Cliente] AND p.Contacto = c.Contacto
     WHERE c.FechaAlta IS NULL;

    SET @hechas = @@ROWCOUNT;
    SET @total += @hechas;
    RAISERROR('Lote: %d fichas (acumulado %d)', 0, 1, @hechas, @total) WITH NOWAIT;
END

SELECT COUNT(*) Total,
       SUM(CASE WHEN FechaAlta IS NULL THEN 1 ELSE 0 END) SinFecha,
       SUM(CASE WHEN AplicacionAlta = 'Backfill' THEN 1 ELSE 0 END) Backfill
FROM dbo.Clientes WITH (NOLOCK);
