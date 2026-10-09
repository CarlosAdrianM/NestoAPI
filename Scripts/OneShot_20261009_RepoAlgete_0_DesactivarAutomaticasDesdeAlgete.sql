/*
    09/10/26 (Carlos): mientras la salida de Ariadna no valide el código al preparar, las reposiciones que SALEN de Algete
    se rellenan y se preparan en Nesto viejo. El job «reposiciones-automaticas» las crea cerradas (para Ariadna) y Nesto
    viejo no puede trabajarlas: se desactivan en el calendario las rutas Algete → Reina y Algete → Alcobendas. Las de las
    tiendas a Algete siguen automáticas. Volver a activarlas: el mismo UPDATE con Activo = 1 (o desde la ventana del
    calendario de Nesto cuando esté publicada). SSMS contra NV como sa.
*/
SET NOCOUNT ON;
USE NV;
GO

UPDATE ReposicionesCalendario SET Activo = 0, Usuario = 'Carlos 09/10/26 (Nesto viejo)', FechaModificacion = GETDATE()
WHERE Empresa = '1' AND AlmacenOrigen = 'ALG' AND Activo = 1;
SELECT @@ROWCOUNT Desactivadas;   -- 6 (ALG→REI L/X/V y ALG→ALC L/M/J)

SELECT RTRIM(AlmacenOrigen) O, RTRIM(AlmacenDestino) D, DiaSemana, Activo FROM ReposicionesCalendario WHERE Empresa = '1' ORDER BY O, D, DiaSemana;
