/*
    NestoAPI#577 (corte 3b): calendario de reposiciones en los dos sentidos (decisión de Carlos, 08/10/26).

      - Algete → Reina y Reina → Algete: lunes, miércoles y viernes.
      - Algete → Alcobendas: lunes, martes y jueves (los mismos días que tenía Alcobendas → Algete: la furgoneta deja una
        y recoge otra).
      - Alcobendas → Algete: de lunes a viernes (dos días a la semana recoge sin dejar).

    Lo que había (Scripts/Issue577_ReposicionesCalendario.sql, ya en producción): REI → ALG 1/3/5 y ALC → ALG 1/2/4, cierre
    10:00 y llegada 13:30. Este script AÑADE las filas que faltan:
      - ALC → ALG: miércoles (3) y viernes (5).
      - ALG → REI: 1, 3, 5.
      - ALG → ALC: 1, 2, 4.

    NestoAPI#606 (decisión de Carlos, 08/10/26): HORA DE LLEGADA POR FILA. Cada reposición (origen → destino × día) tiene su
    propia hora de llegada (ReposicionesCalendario.HoraLlegadaHabitual): Alcobendas puede llegar a otra hora que Reina, y el
    lunes a otra hora que el viernes en la misma tienda. La API la lee de la fila, no hay ninguna hora fija en el código:
      - Tienda → Algete: si la fila llega ANTES de la hora de corte del picking (parámetro «(defecto)» HoraCortePicking,
        11:00, la única fuente), las unidades salen en el picking de ese mismo día; si llega a esa hora o después, en el
        del laborable siguiente (CalculadoraFechaReposicion, fecha de entrega a la agencia de #606).
      - Algete → tienda: es la hora a la que el job «reposiciones-automaticas» deja de reintentar y avisa.
    La tabla @Filas de abajo tiene una fila por reposición con SU hora de llegada: ponerlas aquí antes de lanzar (o después,
    desde el mantenimiento del calendario / PUT api/Reposiciones/Calendario). Hoy todas valen 13:30, que es lo que había;
    PENDIENTE DE CONFIRMAR con Almacén la de cada una.

    Qué hace:
      1) INSERTA las filas de @Filas que no existan (misma ruta, día y hora de cierre), con su hora de llegada.
      2) ACTUALIZA la hora de llegada de las filas que ya existen y siguen como las dejó el script (Usuario = 'NestoAPI#577'),
         si en @Filas pone otra. Las que alguien ya ha cambiado desde el mantenimiento (otro Usuario) no se tocan.

    Horas de cierre de las rutas nuevas (@HoraCierre): 10:00, como las de tienda → Algete. A esa hora el job
    «reposiciones-automaticas» rellena la reposición sola (desde Algete nace cerrada, con los huecos reservados, por recoger
    en Ariadna). Tiene que caer dentro del cron del job (L-V de 6:00 a 21:55).

    Idempotente. Ejecutar en SSMS contra NV (como sa o con un login que pueda escribir en ReposicionesCalendario). Se puede
    lanzar antes o después del deploy; el job solo rellena si también están los scripts del corte 3a (ReposicionesTraspasos
    con la columna Omitida, LinPedidoVta.FechaCreacion y prdRellenarReposicionStock2).
*/

SET NOCOUNT ON;
USE NV;
GO

DECLARE @HoraCierre time(0) = '10:00';     -- cierre de las reposiciones (las que ya existen se buscan por esta hora)

-- Una fila por reposición, con SU hora de llegada. PENDIENTE DE CONFIRMAR con Almacén cada una (hoy, 13:30 todas).
DECLARE @Filas TABLE (Origen char(3), Destino char(3), DiaSemana tinyint, Llegada time(0));
INSERT INTO @Filas (Origen, Destino, DiaSemana, Llegada)
VALUES
    -- Reina → Algete (lunes, miércoles y viernes)
    ('REI', 'ALG', 1, '13:30'), ('REI', 'ALG', 3, '13:30'), ('REI', 'ALG', 5, '13:30'),
    -- Alcobendas → Algete (de lunes a viernes)
    ('ALC', 'ALG', 1, '13:30'), ('ALC', 'ALG', 2, '13:30'), ('ALC', 'ALG', 3, '13:30'), ('ALC', 'ALG', 4, '13:30'), ('ALC', 'ALG', 5, '13:30'),
    -- Algete → Reina (lunes, miércoles y viernes)
    ('ALG', 'REI', 1, '13:30'), ('ALG', 'REI', 3, '13:30'), ('ALG', 'REI', 5, '13:30'),
    -- Algete → Alcobendas (lunes, martes y jueves)
    ('ALG', 'ALC', 1, '13:30'), ('ALG', 'ALC', 2, '13:30'), ('ALG', 'ALC', 4, '13:30');

IF EXISTS (SELECT 1 FROM @Filas WHERE Llegada < @HoraCierre)
BEGIN
    RAISERROR('Hay una hora de llegada anterior a la de cierre: revisar @Filas. No se ha hecho nada.', 16, 1);
    RETURN;
END

INSERT INTO dbo.ReposicionesCalendario (Empresa, AlmacenOrigen, AlmacenDestino, DiaSemana, HoraCierre, HoraLlegadaHabitual, Activo, Usuario, FechaModificacion)
SELECT '1', f.Origen, f.Destino, f.DiaSemana, @HoraCierre, f.Llegada, 1, 'NestoAPI#577', GETDATE()
FROM @Filas f
WHERE NOT EXISTS (SELECT 1 FROM dbo.ReposicionesCalendario c
                  WHERE c.Empresa = '1' AND c.AlmacenOrigen = f.Origen AND c.AlmacenDestino = f.Destino
                    AND c.DiaSemana = f.DiaSemana AND c.HoraCierre = @HoraCierre);

PRINT CONCAT('Filas añadidas: ', @@ROWCOUNT);

UPDATE c
SET c.HoraLlegadaHabitual = f.Llegada, c.FechaModificacion = GETDATE()
FROM dbo.ReposicionesCalendario c
     JOIN @Filas f ON c.AlmacenOrigen = f.Origen AND c.AlmacenDestino = f.Destino AND c.DiaSemana = f.DiaSemana
WHERE c.Empresa = '1' AND c.HoraCierre = @HoraCierre AND c.Usuario = 'NestoAPI#577' AND c.HoraLlegadaHabitual <> f.Llegada;

PRINT CONCAT('Horas de llegada cambiadas: ', @@ROWCOUNT);
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN: 14 filas activas, cada una con su hora de llegada
--   ALC→ALG 1-5 · ALG→ALC 1/2/4 · ALG→REI 1/3/5 · REI→ALG 1/3/5
------------------------------------------------------------------------------------------------
SELECT AlmacenOrigen, AlmacenDestino, DiaSemana, HoraCierre, HoraLlegadaHabitual, Activo, Usuario
FROM dbo.ReposicionesCalendario
WHERE Empresa = '1'
ORDER BY AlmacenOrigen, AlmacenDestino, DiaSemana, HoraCierre;
GO
