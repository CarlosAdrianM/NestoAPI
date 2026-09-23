-- NestoAPI#517: interruptor de POST api/PedidosVenta/OfertasSugeridas (aviso de ofertas no aplicadas
-- en Nesto y NestoApp). El 23/09/26 este endpoint tumbó RDS2016 y no se pudo apagar sin publicar.
--
-- Sin fila = ENCENDIDO (así nace). Este script deja la fila creada a '1' para que el interruptor esté a mano.
-- La API lo relee cada minuto: un cambio aquí se nota en menos de 60 s, sin publicar ni reiniciar.
-- Ejecutar en NV (NestoConnection).
USE NV;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'OfertasSugeridasActivas')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'OfertasSugeridasActivas', '1', 'NestoAPI#517', GETDATE());

-- Comprobación
SELECT Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación]
FROM dbo.ParámetrosUsuario WHERE Clave = 'OfertasSugeridasActivas';
GO

-- APAGAR (urgencia): los clientes dejan de ver el aviso de ofertas; el pedido se sigue validando al guardar.
-- UPDATE dbo.ParámetrosUsuario SET Valor = '0', Usuario2 = 'NestoAPI#517', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'OfertasSugeridasActivas';

-- ENCENDER de nuevo:
-- UPDATE dbo.ParámetrosUsuario SET Valor = '1', Usuario2 = 'NestoAPI#517', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'OfertasSugeridasActivas';
