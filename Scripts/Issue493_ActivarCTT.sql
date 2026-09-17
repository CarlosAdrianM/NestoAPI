-- =============================================================================
-- NestoAPI#493: salida a producción de CTT Express. Ejecutar en SSMS contra NV el día acordado con
-- el comercial de CTT, POR LA TARDE, una vez tramitados los envíos de las demás agencias. Desde ese
-- momento el comparador elige CTT (en las zonas activas), Nesto la ofrece al abrir la ventana de
-- Agencias y el poll de seguimiento la consulta. Al día siguiente el repartidor recoge.
--
-- ANTES (sin prisa, cualquier día previo):
--   - NestoAPI publicada con el corte 2 de #493 (Web.config ya lleva la URL de producción).
--   - secretos.config del servidor: CTTClientId / CTTClientSecret con las credenciales de PRODUCCIÓN
--     (guardadas en el secretos.config local como CTTClientIdProduccion / CTTClientSecretProduccion).
--   - Nesto publicado con AgenciaCTT en el factory (oculta mientras EsSombra = 1).
--   - Rollos blancos de 100x150 en la Zebra que usaba Sending (\\RDS2016\etiquetas1, parámetro
--     ImpresoraAgencia) y una etiqueta de prueba impresa desde Nesto.
--
-- VUELTA ATRÁS: UPDATE AgenciasTransporte SET EsSombra = 1 WHERE Numero = 13;
-- =============================================================================

USE NV;
GO

-- 1. Freno de arranque "de menos a más" (lo pidió CTT): fase 1 solo provincial (Madrid).
--    Fases siguientes: UPDATE del Valor a 'Provincial, Peninsular' y después a '' (todas las zonas).
--    Nombres válidos: Provincial, Peninsular, Portugal, BalearesMayores, BalearesMenores.
IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'CTTZonasActivas')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'CTTZonasActivas', 'Provincial', 'NestoAPI#493', GETDATE());
ELSE
    UPDATE dbo.ParámetrosUsuario SET Valor = 'Provincial', Usuario2 = 'NestoAPI#493', [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'CTTZonasActivas';

-- 2. El interruptor: CTT deja de ser sombra.
UPDATE dbo.AgenciasTransporte SET EsSombra = 0 WHERE Numero = 13;

-- Comprobación
SELECT Numero, Nombre, EsSombra, Identificador, RecargoCombustible FROM dbo.AgenciasTransporte WHERE Numero = 13;
SELECT Clave, Valor FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'CTTZonasActivas';

-- =============================================================================
-- Fases posteriores (cuando CTT y el almacén den el visto bueno a la anterior):
-- UPDATE dbo.ParámetrosUsuario SET Valor = 'Provincial, Peninsular' WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'CTTZonasActivas';
-- UPDATE dbo.ParámetrosUsuario SET Valor = '' WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'CTTZonasActivas';  -- todas
-- =============================================================================
