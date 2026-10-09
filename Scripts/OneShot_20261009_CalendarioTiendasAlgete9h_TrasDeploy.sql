/*
    09/10/26 (Carlos, a petición de Reina y Paloma en Novedades 540/552): las reposiciones de las tiendas a Algete
    (Reina → Algete y Alcobendas → Algete) cierran a las 9:00 en vez de a las 10:00, porque a las 10 muchas veces ya
    está allí quien la recoge y no da tiempo a prepararla. Los pedidos que entren entre las 9 y las 10 van en la
    reposición siguiente. La llegada a Algete (13:30) no cambia.

    CUÁNDO LANZARLO: en cualquier momento, pero SOLO DESPUÉS de publicar la API del 09/10/26 (commit cff45f34, regla del
    «día ya tratado»: una ruta con cabecera del job en el mismo día de cierre no se vuelve a rellenar aunque cambie la
    hora). Con la API anterior, usar OneShot_20261009_CalendarioTiendasAlgete9h.sql (con freno de horario).
    Alternativa: cambiarlo desde Nesto, Productos › Reposición › Calendario (cuando se publique esa versión).

    Solo toca las filas que siguen a las 10:00. Idempotente. SSMS contra NV, como sa.
    Vuelta atrás: el mismo UPDATE con '10:00' (y el mismo freno de horario).
*/
SET NOCOUNT ON;
USE NV;
GO

SELECT 'Antes' Momento, AlmacenOrigen, AlmacenDestino, DiaSemana, HoraCierre, HoraLlegadaHabitual, Usuario
FROM ReposicionesCalendario
WHERE Empresa = '1' AND AlmacenDestino = 'ALG' AND AlmacenOrigen IN ('REI', 'ALC')
ORDER BY AlmacenOrigen, DiaSemana;

UPDATE ReposicionesCalendario
SET HoraCierre = CAST('09:00' AS time), Usuario = 'Carlos 09/10/26 (9h)', FechaModificacion = GETDATE()
WHERE Empresa = '1' AND AlmacenDestino = 'ALG' AND AlmacenOrigen IN ('REI', 'ALC') AND HoraCierre = CAST('10:00' AS time);

SELECT @@ROWCOUNT Cambiadas;   -- 8 la primera vez (REI 1/3/5 y ALC 1-5), 0 si ya estaba hecho

SELECT 'Después' Momento, AlmacenOrigen, AlmacenDestino, DiaSemana, HoraCierre, HoraLlegadaHabitual, Usuario
FROM ReposicionesCalendario
WHERE Empresa = '1' AND AlmacenDestino = 'ALG' AND AlmacenOrigen IN ('REI', 'ALC')
ORDER BY AlmacenOrigen, DiaSemana;
