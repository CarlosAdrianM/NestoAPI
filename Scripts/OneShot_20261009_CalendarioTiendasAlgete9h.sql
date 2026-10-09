/*
    09/10/26 (Carlos, a petición de Reina y Paloma en Novedades 540/552): las reposiciones de las tiendas a Algete
    (Reina → Algete y Alcobendas → Algete) cierran a las 9:00 en vez de a las 10:00, porque a las 10 muchas veces ya
    está allí quien la recoge y no da tiempo a prepararla. Los pedidos que entren entre las 9 y las 10 van en la
    reposición siguiente. La llegada a Algete (13:30) no cambia.

    CUÁNDO LANZARLO: fuera del horario del job «reposiciones-automaticas» (L-V de 6:00 a 21:55), es decir, a partir de
    las 22:00 de un laborable o en fin de semana. Si se cambia la hora de cierre de hoy mientras el job corre, verá un
    corte nuevo de hoy (las 9:00) sin cabecera y volverá a intentar rellenar las de hoy o avisará de «fuera de plazo».
    El script se niega a cambiar nada en esa franja.

    Solo toca las filas que siguen a las 10:00. Idempotente. SSMS contra NV, como sa.
    Vuelta atrás: el mismo UPDATE con '10:00' (y el mismo freno de horario).
*/
SET NOCOUNT ON;
USE NV;
GO

DECLARE @ahora datetime = GETDATE();
DECLARE @diaIso int = (DATEPART(weekday, @ahora) + @@DATEFIRST - 2) % 7 + 1;   -- 1 = lunes … 7 = domingo
IF @diaIso BETWEEN 1 AND 5 AND CAST(@ahora AS time) >= '06:00' AND CAST(@ahora AS time) < '22:00'
BEGIN
    RAISERROR('Ahora corre el job de reposiciones (L-V de 6:00 a 21:55): lánzalo a partir de las 22:00 o en fin de semana. No se ha cambiado nada.', 16, 1);
    RETURN;
END

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
