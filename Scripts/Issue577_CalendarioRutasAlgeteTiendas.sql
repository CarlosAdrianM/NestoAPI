/*
    NestoAPI#577 (corte 3b, 3d): calendario de reposiciones en los dos sentidos (decisiones de Carlos, 08/10/26).

      - Reina → Algete: lunes, miércoles y viernes. Cierre 10:00, llega a Algete a las 13:30 el MISMO día (antelación 0).
      - Alcobendas → Algete: de lunes a viernes. Cierre 10:00, llega a las 13:30 el mismo día (antelación 0).
      - Algete → Reina: llega lunes, miércoles y viernes sobre las 11:00. Se CIERRA EL LABORABLE ANTERIOR a las 13:00
        (antelación 1): la del lunes, el viernes; la del miércoles, el martes; la del viernes, el jueves.
      - Algete → Alcobendas: llega lunes, martes y jueves a las 10:00. Se cierra el laborable anterior a las 13:00
        (antelación 1): la del lunes, el viernes; la del martes, el lunes; la del jueves, el miércoles.

    Modelo (corte 3d): DiaSemana es el día de LLEGADA (el de la ruta); LaborablesAntelacionCierre, cuántos laborables del
    origen antes se cierra a HoraCierre (con festivos en el origen, el laborable anterior). A la hora de cierre el job
    «reposiciones-automaticas» rellena la reposición sola (desde Algete nace cerrada, con los huecos reservados, por
    recoger en Ariadna), con ese instante como corte. Las horas de cierre (10:00 y 13:00) caen dentro del cron del job
    (L-V de 6:00 a 21:55).

    Lo que había (Scripts/Issue577_ReposicionesCalendario.sql, ya en producción): REI → ALG 1/3/5 y ALC → ALG 1/2/4, cierre
    10:00 y llegada 13:30. Este script AÑADE las filas que faltan (ALC → ALG 3 y 5; ALG → REI 1/3/5; ALG → ALC 1/2/4).

    NestoAPI#606: la hora de llegada es POR FILA (ReposicionesCalendario.HoraLlegadaHabitual); la API la lee de la fila:
      - Tienda → Algete: si la fila llega ANTES de la hora de corte del picking (parámetro «(defecto)» HoraCortePicking,
        11:00), las unidades salen en el picking de ese mismo día; si llega a esa hora o después, en el del laborable
        siguiente.
      - Algete → tienda: es la hora a la que el job deja de reintentar y avisa (fuera de plazo).

    Qué hace:
      1) INSERTA las filas de @Filas cuya ruta y día (de llegada) no tengan ya fila.
      2) ACTUALIZA hora de cierre, hora de llegada y antelación de las filas que siguen como las dejó el script
         (Usuario = 'NestoAPI#577') si en @Filas pone otra cosa. Las que alguien ya ha cambiado desde el mantenimiento
         (otro Usuario) no se tocan.

    ORDEN: DESPUÉS de Scripts/Issue577_CalendarioCierreAntelacion.sql (la columna LaborablesAntelacionCierre). Si falta,
    no hace nada. Idempotente. Ejecutar en SSMS contra NV (como sa o con un login que pueda escribir en
    ReposicionesCalendario). El job solo rellena si también están los scripts del corte 3a (ReposicionesTraspasos con la
    columna Omitida, LinPedidoVta.FechaCreacion y prdRellenarReposicionStock2).
*/

SET NOCOUNT ON;
USE NV;
GO

IF COL_LENGTH('dbo.ReposicionesCalendario', 'LaborablesAntelacionCierre') IS NULL
BEGIN
    RAISERROR('Falta la columna LaborablesAntelacionCierre: lanzar antes Scripts/Issue577_CalendarioCierreAntelacion.sql. No se ha hecho nada.', 16, 1);
    SET NOEXEC ON;
END
GO

-- Una fila por reposición (día de LLEGADA), con SU hora de cierre, de llegada y los laborables de antelación del cierre.
DECLARE @Filas TABLE (Origen char(3), Destino char(3), DiaSemana tinyint, Cierre time(0), Llegada time(0), Antelacion tinyint);
INSERT INTO @Filas (Origen, Destino, DiaSemana, Cierre, Llegada, Antelacion)
VALUES
    -- Reina → Algete (lunes, miércoles y viernes): cierra 10:00, llega 13:30 el mismo día
    ('REI', 'ALG', 1, '10:00', '13:30', 0), ('REI', 'ALG', 3, '10:00', '13:30', 0), ('REI', 'ALG', 5, '10:00', '13:30', 0),
    -- Alcobendas → Algete (de lunes a viernes): cierra 10:00, llega 13:30 el mismo día
    ('ALC', 'ALG', 1, '10:00', '13:30', 0), ('ALC', 'ALG', 2, '10:00', '13:30', 0), ('ALC', 'ALG', 3, '10:00', '13:30', 0),
    ('ALC', 'ALG', 4, '10:00', '13:30', 0), ('ALC', 'ALG', 5, '10:00', '13:30', 0),
    -- Algete → Reina (llega lunes, miércoles y viernes a las 11:00): cierra el laborable anterior a las 13:00
    ('ALG', 'REI', 1, '13:00', '11:00', 1), ('ALG', 'REI', 3, '13:00', '11:00', 1), ('ALG', 'REI', 5, '13:00', '11:00', 1),
    -- Algete → Alcobendas (llega lunes, martes y jueves a las 10:00): cierra el laborable anterior a las 13:00
    ('ALG', 'ALC', 1, '13:00', '10:00', 1), ('ALG', 'ALC', 2, '13:00', '10:00', 1), ('ALG', 'ALC', 4, '13:00', '10:00', 1);

-- Con antelación 0 llega el mismo día: la llegada no puede ser anterior al cierre (como el CHECK de la tabla).
IF EXISTS (SELECT 1 FROM @Filas WHERE Antelacion = 0 AND Llegada < Cierre)
BEGIN
    RAISERROR('Hay una fila que llega el mismo día (antelación 0) antes de cerrarse: revisar @Filas. No se ha hecho nada.', 16, 1);
    RETURN;
END

INSERT INTO dbo.ReposicionesCalendario (Empresa, AlmacenOrigen, AlmacenDestino, DiaSemana, HoraCierre, HoraLlegadaHabitual,
    LaborablesAntelacionCierre, Activo, Usuario, FechaModificacion)
SELECT '1', f.Origen, f.Destino, f.DiaSemana, f.Cierre, f.Llegada, f.Antelacion, 1, 'NestoAPI#577', GETDATE()
FROM @Filas f
WHERE NOT EXISTS (SELECT 1 FROM dbo.ReposicionesCalendario c
                  WHERE c.Empresa = '1' AND c.AlmacenOrigen = f.Origen AND c.AlmacenDestino = f.Destino
                    AND c.DiaSemana = f.DiaSemana);

PRINT CONCAT('Filas añadidas: ', @@ROWCOUNT);

UPDATE c
SET c.HoraCierre = f.Cierre, c.HoraLlegadaHabitual = f.Llegada, c.LaborablesAntelacionCierre = f.Antelacion,
    c.FechaModificacion = GETDATE()
FROM dbo.ReposicionesCalendario c
     JOIN @Filas f ON c.AlmacenOrigen = f.Origen AND c.AlmacenDestino = f.Destino AND c.DiaSemana = f.DiaSemana
WHERE c.Empresa = '1' AND c.Usuario = 'NestoAPI#577'
  AND (c.HoraCierre <> f.Cierre OR c.HoraLlegadaHabitual <> f.Llegada OR c.LaborablesAntelacionCierre <> f.Antelacion)
  -- una sola fila de esa ruta y día (si hubiera dos, con horas de cierre distintas, se dejan como están)
  AND (SELECT COUNT(*) FROM dbo.ReposicionesCalendario o
       WHERE o.Empresa = c.Empresa AND o.AlmacenOrigen = c.AlmacenOrigen AND o.AlmacenDestino = c.AlmacenDestino
         AND o.DiaSemana = c.DiaSemana) = 1;

PRINT CONCAT('Filas cambiadas: ', @@ROWCOUNT);
GO

SET NOEXEC OFF;
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN: 14 filas activas
--   ALC→ALG 1-5 y REI→ALG 1/3/5: 10:00 → 13:30, antelación 0
--   ALG→REI 1/3/5: 13:00 → 11:00, antelación 1 · ALG→ALC 1/2/4: 13:00 → 10:00, antelación 1
------------------------------------------------------------------------------------------------
SELECT AlmacenOrigen, AlmacenDestino, DiaSemana, HoraCierre, HoraLlegadaHabitual, LaborablesAntelacionCierre, Activo, Usuario
FROM dbo.ReposicionesCalendario
WHERE Empresa = '1'
ORDER BY AlmacenOrigen, AlmacenDestino, DiaSemana, HoraCierre;
GO
