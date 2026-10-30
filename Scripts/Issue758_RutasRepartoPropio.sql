-- NestoAPI#758 (corte 2 de #755): la lista de rutas que son REPARTO PROPIO sale del parámetro RutasRepartoPropio
-- (ParametrosUsuario, usuario «(defecto)», empresa 1). La API lo lee cada 5 minutos (sin publicar); sin la fila, o vacía,
-- son AT y 16 como hasta ahora. Separadores admitidos: coma, punto y coma o espacio.
--
-- Esta lista la usan TODOS los sitios de la API que deciden «ruta propia»: Facturar rutas (2 copias), la agencia 14 en la
-- propuesta de envío y el packing, los días de entrega del picking, el picking de rutas propias, las sugerencias de ruta y
-- las paradas de las notas de entrega. OJO: añadir 01-05 cambia la agencia propuesta y el packing de todos los clientes
-- de esas rutas (unos 30.000 en Madrid). Nesto (VB) tiene sus propias listas: revisar antes de añadirlas.
--
-- Lanzar como sa en NV. Idempotente: crea la fila con el valor de siempre (no cambia nada) y no pisa una que ya exista.

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.ParametrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RutasRepartoPropio')
BEGIN
    INSERT INTO dbo.ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'RutasRepartoPropio', '(defecto)', 'AT,16', 'Issue758', GETDATE());
    PRINT 'Creado RutasRepartoPropio = AT,16 (como hasta ahora).';
END
ELSE
    PRINT 'RutasRepartoPropio ya existe: no se toca.';

SELECT Empresa, Usuario, Clave, RTRIM(Valor) AS Valor, Usuario2, [Fecha Modificación]
FROM dbo.ParametrosUsuario
WHERE Clave = 'RutasRepartoPropio';

-- Cuando Carlos decida pasar las rutas de Madrid (01 lunes … 05 viernes) a reparto propio, descomentar:
-- UPDATE dbo.ParametrosUsuario
--    SET Valor = 'AT,16,01,02,03,04,05', Usuario2 = SUSER_SNAME(), [Fecha Modificación] = GETDATE()
--  WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RutasRepartoPropio';
-- Para volver atrás: Valor = 'AT,16'. La API lo nota en 5 minutos como mucho.
