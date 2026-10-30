/*
    NestoAPI#763 (corte 2): carga inicial de las PROPUESTAS de horario preferible desde los comentarios de la ficha.

    Hoy no hay ningún campo de horario: unos 121 clientes activos (123 contactos el 30/10/26) lo tienen escrito en Comentarios
    o en ComentarioRuta («Horario: 9 a 13:30 / 16:00 a 22:00», «HORARIO DE 9 A 2 Y DE 4:30 A 8»…). Este script copia ese texto
    a dbo.ClientesHorarioPreferiblePropuestas en estado «PorRevisar». El horario propuesto lo calculan las reglas de la API al
    listarlas (GET api/Clientes/HorariosPreferibles/Propuestas) y NO cuenta para nada hasta que una persona lo acepta (o lo
    corrige y lo acepta) en Nesto: Clientes > «Horarios por revisar», o en la pestaña «Horario preferible» de la ficha.

    @Aplicar = 0 (por defecto): SOLO ENSEÑA lo que cargaría (no escribe nada).
    @Aplicar = 1: inserta las propuestas que falten. Idempotente: no repite las que ya están (en cualquier estado) ni propone
    a quien ya tiene horario preferible.

    NO TOCA dbo.Clientes (solo la lee con NOLOCK): ni trigger trgClientesUpd ni bloqueos sobre Clientes. Las filas son ~120, así
    que no hace falta ir por lotes.

    Requisito: Scripts/Issue763_HorarioPreferible.sql. Ejecutar en SSMS contra NV como sa.
    Deshacer (solo las que siguen por revisar):
        DELETE FROM dbo.ClientesHorarioPreferiblePropuestas WHERE Origen = 'Comentarios' AND Estado = 'PorRevisar';
*/
SET NOCOUNT ON;
USE NV;

DECLARE @Aplicar bit = 0;

IF OBJECT_ID('dbo.ClientesHorarioPreferiblePropuestas') IS NULL OR OBJECT_ID('dbo.ClientesHorarioPreferible') IS NULL
BEGIN
    RAISERROR('Faltan las tablas: lance antes Scripts/Issue763_HorarioPreferible.sql.', 16, 1);
    RETURN;
END

-- Las fichas activas (Estado >= 0) con «horario» en los comentarios. El separador «||» entre los dos campos lo usan las reglas.
SELECT c.Empresa, c.[Nº Cliente] AS Cliente, c.Contacto,
       LEFT(ISNULL(CAST(c.Comentarios AS nvarchar(max)), N'') + N' || ' + ISNULL(RTRIM(c.ComentarioRuta), N''), 2000) AS TextoOrigen,
       RTRIM(c.Nombre) AS Nombre, RTRIM(c.Ruta) AS Ruta
INTO #Candidatas
FROM dbo.Clientes c WITH (NOLOCK)
WHERE c.Empresa = '1' AND c.Estado >= 0          -- las fichas de las otras empresas son copias de las de la 1
  AND (c.Comentarios LIKE N'%horario%' OR c.ComentarioRuta LIKE '%horario%')
  AND NOT EXISTS (SELECT 1 FROM dbo.ClientesHorarioPreferiblePropuestas p
                   WHERE p.Empresa = c.Empresa AND p.Cliente = c.[Nº Cliente] AND p.Contacto = c.Contacto AND p.Origen = 'Comentarios')
  AND NOT EXISTS (SELECT 1 FROM dbo.ClientesHorarioPreferible h
                   WHERE h.Empresa = c.Empresa AND h.Cliente = c.[Nº Cliente] AND h.Contacto = c.Contacto);

SELECT COUNT(*) AS Propuestas, COUNT(DISTINCT Empresa + Cliente) AS Clientes FROM #Candidatas;
SELECT RTRIM(Empresa) AS Empresa, RTRIM(Cliente) AS Cliente, RTRIM(Contacto) AS Contacto, Nombre, Ruta, TextoOrigen
FROM #Candidatas ORDER BY Ruta, Cliente, Contacto;

IF @Aplicar = 1
BEGIN
    INSERT INTO dbo.ClientesHorarioPreferiblePropuestas (Empresa, Cliente, Contacto, Origen, TextoOrigen, Estado, Fecha)
    SELECT Empresa, Cliente, Contacto, 'Comentarios', TextoOrigen, 'PorRevisar', GETDATE()
    FROM #Candidatas;
    PRINT CONCAT('NestoAPI#763: ', @@ROWCOUNT, ' propuestas cargadas por revisar.');
END
ELSE
    PRINT 'NestoAPI#763: @Aplicar = 0, solo se enseña lo que se cargaría (no se ha escrito nada).';

DROP TABLE #Candidatas;
