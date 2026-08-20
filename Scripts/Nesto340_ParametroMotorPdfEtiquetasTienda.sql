-- Nesto#340 (Fase 2, 20/08/26): activar las etiquetas de tienda por QuestPDF SOLO para el
-- usuario piloto (Carlos). Papel físico de etiquetas precortadas: comparar contra el papel
-- real antes de extender a más usuarios (mismo circuito que MotorPdfExtractoContable).
-- Sin fila (o con otro valor) se usa el RDLC local de siempre: vuelta atrás = DELETE.
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario
               WHERE Empresa = '1' AND Clave = 'MotorPdfEtiquetasTienda' AND Usuario = 'NUEVAVISION\Carlos')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'MotorPdfEtiquetasTienda', 'NUEVAVISION\Carlos', 'QuestPDF', 'NUEVAVISION\Carlos', GETDATE());
END
