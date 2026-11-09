/*
    NestoAPI#848 (09/11/26, sugerencia 1128 de Laura Villacieros): ORÍGENES DE CANAL Y ORIGEN PROPUESTO POR USUARIO.

    1. dbo.OrigenesCliente.EsLead (bit, DEFAULT 1): 1 = campaña (lead que hay que llamar: job «leads-sin-llamar», sugerencias de
       contacto, importador de leads de Meta); 0 = canal (un dato para medir, nadie tiene que llamar). La fila que ya hay
       (META-HARMONIA-0926) se queda en 1 por el DEFAULT.
    2. Orígenes de canal nuevos (EsLead = 0): Página web, Amazon, Tienda de Reina, Tienda de Alcobendas y Cursos.
       HorasParaLlamar = 24 solo por la CHECK (> 0): en los de canal no se usa.
    3. ParametrosUsuario «OrigenClientePorDefecto» (empresa 1): el origen que el alta de clientes PROPONE según quién lo crea.
       Siempre se elige a mano; es solo la propuesta. Fila «(defecto)» vacía (= sin propuesta) y las de Paloma, Reina y Pilar
       (usuarios sin dominio, como el resto de sus parámetros). La API no le crea fila a nadie al leerlo.

    Lanzar como sa en NV ANTES de publicar la API (la API nueva lee la columna EsLead: sin ella, el catálogo sale vacío).
    Idempotente. La columna va en su propio lote y con EXEC (el lote entero se compila antes de ejecutarse).
    Sin GRANTs nuevos: RDS2016$ ya tiene SELECT/INSERT/UPDATE/DELETE en dbo.OrigenesCliente (Issue623).
*/
SET NOCOUNT ON;
USE NV;
GO
-- 1. Columna EsLead (en su propio lote)
IF COL_LENGTH(N'dbo.OrigenesCliente', N'EsLead') IS NULL
BEGIN
    EXEC(N'ALTER TABLE dbo.OrigenesCliente ADD EsLead bit NOT NULL CONSTRAINT DF_OrigenesCliente_EsLead DEFAULT (1)');
    PRINT 'OrigenesCliente: añadida EsLead (1 en las filas que ya había).';
END
GO
-- 2. Orígenes de canal (no son leads)
IF NOT EXISTS (SELECT 1 FROM dbo.OrigenesCliente WHERE Codigo = 'WEB')
    INSERT INTO dbo.OrigenesCliente (Codigo, Descripcion, Campana, FechaDesde, FechaHasta, Activo, HorasParaLlamar, EsLead, Usuario)
    VALUES ('WEB', N'Página web', NULL, NULL, NULL, 1, 24, 0, N'Issue848');
IF NOT EXISTS (SELECT 1 FROM dbo.OrigenesCliente WHERE Codigo = 'AMAZON')
    INSERT INTO dbo.OrigenesCliente (Codigo, Descripcion, Campana, FechaDesde, FechaHasta, Activo, HorasParaLlamar, EsLead, Usuario)
    VALUES ('AMAZON', N'Amazon', NULL, NULL, NULL, 1, 24, 0, N'Issue848');
IF NOT EXISTS (SELECT 1 FROM dbo.OrigenesCliente WHERE Codigo = 'TIENDA-REINA')
    INSERT INTO dbo.OrigenesCliente (Codigo, Descripcion, Campana, FechaDesde, FechaHasta, Activo, HorasParaLlamar, EsLead, Usuario)
    VALUES ('TIENDA-REINA', N'Tienda de Reina', NULL, NULL, NULL, 1, 24, 0, N'Issue848');
IF NOT EXISTS (SELECT 1 FROM dbo.OrigenesCliente WHERE Codigo = 'TIENDA-ALCOBENDAS')
    INSERT INTO dbo.OrigenesCliente (Codigo, Descripcion, Campana, FechaDesde, FechaHasta, Activo, HorasParaLlamar, EsLead, Usuario)
    VALUES ('TIENDA-ALCOBENDAS', N'Tienda de Alcobendas', NULL, NULL, NULL, 1, 24, 0, N'Issue848');
IF NOT EXISTS (SELECT 1 FROM dbo.OrigenesCliente WHERE Codigo = 'CURSOS')
    INSERT INTO dbo.OrigenesCliente (Codigo, Descripcion, Campana, FechaDesde, FechaHasta, Activo, HorasParaLlamar, EsLead, Usuario)
    VALUES ('CURSOS', N'Cursos', NULL, NULL, NULL, 1, 24, 0, N'Issue848');
GO
-- 3. Origen propuesto por usuario: primero la fila «(defecto)» (vacía = sin propuesta)
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'OrigenClientePorDefecto' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'OrigenClientePorDefecto', '(defecto)', '', 'NestoAPI', GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'OrigenClientePorDefecto' AND Usuario = 'Paloma')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'OrigenClientePorDefecto', 'Paloma', 'TIENDA-ALCOBENDAS', 'NestoAPI', GETDATE());
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'OrigenClientePorDefecto' AND Usuario = 'Reina')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'OrigenClientePorDefecto', 'Reina', 'TIENDA-REINA', 'NestoAPI', GETDATE());
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'OrigenClientePorDefecto' AND Usuario = 'Pilar')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'OrigenClientePorDefecto', 'Pilar', 'CURSOS', 'NestoAPI', GETDATE());
GO
-- Comprobación
SELECT Codigo, Descripcion, Campana, Activo, HorasParaLlamar, EsLead, Usuario, FechaModificacion FROM dbo.OrigenesCliente ORDER BY EsLead DESC, Codigo;
SELECT Empresa, Clave, Usuario, RTRIM(Valor) AS Valor FROM ParametrosUsuario WHERE Clave = 'OrigenClientePorDefecto' ORDER BY Usuario;
GO
