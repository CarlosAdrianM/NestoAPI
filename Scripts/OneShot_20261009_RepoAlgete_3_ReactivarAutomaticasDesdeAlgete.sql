/*
    09/10/26 (Carlos): vuelven a activarse las reposiciones automáticas que SALEN de Algete (Algete → Reina y
    Algete → Alcobendas), que se desactivaron a las 13:52 con OneShot_20261009_RepoAlgete_0_DesactivarAutomaticasDesdeAlgete.sql.
    Desde Ariadna 2.4 (canal interno) la salida es guiada: el código comprueba el producto que toca.

    CUÁNDO LANZARLO: DESPUÉS de que Algete haya contabilizado en Nesto viejo la reposición de Alcobendas (que «General»
    esté vacío). Si se activa antes, el job vería en «General» la de Alcobendas en preparación, daría la de Reina del lunes
    por «ya en preparación» y la apuntaría como omitida: el lunes no se rellenaría sola. El script se niega si «General»
    no está vacío.

    Al activarse, en la siguiente pasada (cada 5 minutos, L-V de 6:00 a 21:55) el job rellena la de Reina del lunes
    (corte de hoy a las 13:00, llega el lunes a las 11:00): nace CERRADA para prepararla en Ariadna. La de Alcobendas del
    lunes ya tiene cabecera (la 80938, reabierta para Nesto viejo) y no se repite. SSMS contra NV como sa.
*/
SET NOCOUNT ON;
USE NV;
GO

IF EXISTS (SELECT 1 FROM PreExtrProducto WHERE Empresa = '1' AND Diario IN ('General', 'RepoEscond'))
BEGIN
    SELECT RTRIM(Diario) Diario, RTRIM([Almacén]) Alm, COUNT(*) Lineas FROM PreExtrProducto WHERE Empresa = '1' AND Diario IN ('General', 'RepoEscond') GROUP BY Diario, [Almacén];
    RAISERROR('Todavía hay líneas en General o RepoEscond: espera a que Algete contabilice la de Alcobendas en Nesto viejo. No se ha cambiado nada.', 16, 1);
    RETURN;
END

UPDATE ReposicionesCalendario SET Activo = 1, Usuario = 'Carlos 09/10/26 (Ariadna 2.4)', FechaModificacion = GETDATE()
WHERE Empresa = '1' AND AlmacenOrigen = 'ALG' AND Activo = 0 AND Usuario = 'Carlos 09/10/26 (Nesto viejo)';
SELECT @@ROWCOUNT Reactivadas;   -- 6

SELECT RTRIM(AlmacenOrigen) O, RTRIM(AlmacenDestino) D, DiaSemana, Activo FROM ReposicionesCalendario WHERE Empresa = '1' ORDER BY O, D, DiaSemana;
