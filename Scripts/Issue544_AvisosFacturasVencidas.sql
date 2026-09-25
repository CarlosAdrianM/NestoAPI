-- NestoAPI#544 (corte 2 de #534): memoria del aviso de facturas vencidas por transferencia.
--
-- Una fila por efecto (Nº_Orden de ExtractoCliente) y aviso mandado DE VERDAD. El modo Sombra
-- la LEE (para decir qué número de aviso sería y cuándo tocaría el siguiente) pero NO escribe:
-- la memoria real empieza cuando el parámetro AvisoFacturasVencidas pase a 'Activo'.
--
-- Cadencia (decisión de Carlos, 25/09/26), contando desde el aviso anterior:
--   1.º al cumplir el umbral (AvisoFacturasVencidasDias), 2.º a los 10 días, 3.º a los 5, 4.º a los 2,
--   y desde ahí diario (el job corre de lunes a viernes a las 7:45).
-- El reloj se reinicia si el cliente paga parte (el pendiente de hoy es menor que el del último aviso)
-- o si administración liquida el efecto (deja de estar pendiente; si se desliquida nace con otro
-- Nº_Orden y empieza de cero).
--
-- ⚠️ EJECUTAR EN PRODUCCIÓN (NV) ANTES DE DESPLEGAR LA API: en Sombra el job la consulta al arrancar
-- la pasada; si no existe, ese día trata a todos como primer aviso y lo apunta en ELMAH.
-- Idempotente.
USE NV;
GO

IF OBJECT_ID('dbo.AvisosFacturasVencidas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AvisosFacturasVencidas (
        Id int IDENTITY(1,1) NOT NULL,
        Empresa char(3) NOT NULL,
        Cliente char(10) NOT NULL,
        Contacto char(3) NOT NULL,
        NumOrden int NOT NULL,              -- ExtractoCliente.Nº_Orden del efecto avisado
        Factura char(10) NULL,
        NumeroAviso int NOT NULL,           -- 1, 2, 3... (se reinicia si baja el pendiente)
        Fecha datetime NOT NULL,            -- día del aviso
        ImportePendiente money NOT NULL,    -- lo que se reclamaba ese día (para detectar pagos parciales)
        Destinatarios nvarchar(500) NULL,
        Usuario nvarchar(30) NOT NULL CONSTRAINT DF_AvisosFacturasVencidas_Usuario DEFAULT SUSER_SNAME(),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_AvisosFacturasVencidas_FechaModificacion DEFAULT GETDATE(),
        CONSTRAINT PK_AvisosFacturasVencidas PRIMARY KEY (Id)
    );
    CREATE INDEX IX_AvisosFacturasVencidas_Efecto ON dbo.AvisosFacturasVencidas (Empresa, NumOrden, Fecha DESC);
    CREATE INDEX IX_AvisosFacturasVencidas_Cliente ON dbo.AvisosFacturasVencidas (Empresa, Cliente, Contacto, Fecha DESC);
END
GO

-- La API entra con la cuenta de máquina (ver feedback de GRANTs de la BD NV).
GRANT SELECT, INSERT ON dbo.AvisosFacturasVencidas TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT COUNT(*) AS Avisos FROM dbo.AvisosFacturasVencidas;
GO

-- ==========================================================================================
-- ENCENDER DE VERDAD (NO ejecutar hasta revisar la sombra del corte 2). Con 'Activo' el job:
--   1) escribe a cada cliente (un correo con todas sus facturas vencidas, PDF adjuntos, desde
--      administracion@ con CCO a administracion@),
--   2) apunta cada efecto avisado en esta tabla,
--   3) manda a administración el resumen del día y los clientes con cobros/abonos por liquidar.
-- UPDATE dbo.ParámetrosUsuario SET Valor = 'Activo', Usuario2 = 'NestoAPI#544', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'AvisoFacturasVencidas';
--
-- VOLVER A SOMBRA: mismo UPDATE con Valor = 'Sombra'. APAGAR: Valor = '0'.
-- ==========================================================================================
