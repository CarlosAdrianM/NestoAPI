-- Nesto#490 / Nesto#340 (13/10/26): motor del PDF del mandato SEPA (botón «Imprimir mandato» de Clientes).
-- Nesto lee MotorPdfMandatoSepa del usuario: con 'QuestPDF' pide api/Clientes/MandatoPDF?...&motor=QuestPDF
-- (generador nuevo, GeneradorPdfMandatoSepa); con cualquier otro valor, el RDLC de siempre.
-- El mandato va al CLIENTE: no se cambia de golpe. Fila (defecto) = 'RDLC' para que el parámetro exista para
-- todos, y se activa usuario a usuario con su propia fila (abajo, comentado) tras comparar los dos PDF.
-- Cuando todos estén en QuestPDF semanas sin quejas: retirar el flag y el RDLC (MandatoCoreSEPA.rdlc).
--
-- BD: NV. Sin GRANTs (INSERT de datos). Idempotente. Ejecutar antes o después del deploy (sin fila → RDLC).

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'MotorPdfMandatoSepa' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'MotorPdfMandatoSepa', '(defecto)', 'RDLC', 'NestoAPI', GETDATE());
END
GO

-- Piloto (Carlos, 13/10/26): solo él con QuestPDF; usuario como lo guardan los demás MotorPdf* («Carlos», sin dominio).
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'MotorPdfMandatoSepa' AND Usuario = 'Carlos')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'MotorPdfMandatoSepa', 'Carlos', 'QuestPDF', 'NestoAPI', GETDATE());
-- GO

-- VERIFICACIÓN:
SELECT Empresa, Clave, Usuario, Valor FROM ParametrosUsuario WHERE Clave = 'MotorPdfMandatoSepa';
GO
