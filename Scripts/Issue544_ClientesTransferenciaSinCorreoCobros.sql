-- NestoAPI#544 (a): clientes financiados por transferencia a plazo (FormaPago TRN y plazos distintos
-- de PRE, CONTADO y CR) SIN persona de contacto activa con correo y cargo Cobros (1) o Factura por
-- correo (22). A estos no les puede llegar ni la factura ni el aviso de pago: administración tiene
-- que completar la ficha (o cambiar la forma de pago).
--
-- El 25/09/26 salían 31 filas (empresa 1, clientes activos): 22828, 2405, 27593/73-87 (Chanel/El Corte
-- Inglés) y 29440/0-16 (Amazon). Solo lectura. Ejecutar en NV.
USE NV;
GO

SELECT c.[Nº Cliente] AS Cliente, c.Contacto, RTRIM(c.Nombre) AS Nombre, RTRIM(cp.FormaPago) AS FormaPago,
       RTRIM(cp.PlazosPago) AS PlazosPago, RTRIM(c.Vendedor) AS Vendedor,
       (SELECT COUNT(*) FROM dbo.PersonasContactoCliente p WITH (NOLOCK)
         WHERE p.Empresa = c.Empresa AND p.NºCliente = c.[Nº Cliente] AND p.Contacto = c.Contacto AND p.Estado >= 0) AS PersonasActivas
FROM dbo.Clientes c WITH (NOLOCK)
INNER JOIN dbo.CondPagoClientes cp WITH (NOLOCK)
    ON cp.Empresa = c.Empresa AND cp.[Nº Cliente] = c.[Nº Cliente] AND cp.Contacto = c.Contacto
WHERE c.Empresa = '1'
  AND c.Estado >= 0
  AND RTRIM(cp.FormaPago) = 'TRN'
  AND RTRIM(cp.PlazosPago) NOT IN ('PRE', 'CONTADO', 'CR')
  AND NOT EXISTS (
        SELECT 1 FROM dbo.PersonasContactoCliente p WITH (NOLOCK)
        WHERE p.Empresa = c.Empresa AND p.NºCliente = c.[Nº Cliente] AND p.Contacto = c.Contacto
          AND p.Estado >= 0
          AND p.CorreoElectrónico IS NOT NULL AND LTRIM(RTRIM(p.CorreoElectrónico)) <> ''
          AND p.Cargo IN (1, 22))
ORDER BY c.[Nº Cliente], c.Contacto;
GO

-- ==========================================================================================
-- INTERRUPTOR de la validación en la API (NO ejecutar hasta que Nesto, NestoApp y TNV manden la
-- persona de contacto en el alta, o se acepte que un alta TRN a plazo sin correo devuelva 400).
-- Con la fila a '1', el POST/PUT de cliente con TRN a plazo sin correo de cobros devuelve 400 con el
-- motivo. Sin fila (así nace) o con otro valor, no se exige nada. Se lee en cada petición.
--
-- IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'ExigirCorreoCobrosTransferencia')
--     INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
--     VALUES ('1', '(defecto)', 'ExigirCorreoCobrosTransferencia', '1', 'NestoAPI#544', GETDATE());
-- ELSE
--     UPDATE dbo.ParámetrosUsuario SET Valor = '1', Usuario2 = 'NestoAPI#544', [Fecha Modificación] = GETDATE()
--     WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'ExigirCorreoCobrosTransferencia';
--
-- APAGAR: mismo UPDATE con Valor = '0'.
-- ==========================================================================================
