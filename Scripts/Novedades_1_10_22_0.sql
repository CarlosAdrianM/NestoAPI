-- =============================================================================
-- Novedades de la version 1.10.22.0 (21/08/2026)
--
-- Solo lo que el usuario NOTA. Se dejan fuera a proposito los cambios internos
-- de la sesion (endpoint del pedido para Agencias, retirada de RDLC/ReportViewer,
-- subida de Informes a net8): no cambian nada de lo que ve o hace la gente.
-- =============================================================================
USE NV
GO

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Fecha_Modificación)
VALUES

-- ---------- Nesto ----------
('1.10.22.0', '2026-08-21', 'Corregido',
 'Plantilla: desmarcar "Servir junto" no llegaba al pedido',
 'Si desmarcabas "Servir junto" (o marcabas "Mantener junto") en la plantilla de venta, la pantalla lo mostraba bien pero el pedido se creaba con el valor antiguo, sin ningun aviso. Ya se guarda lo que ves. Los pedidos creados asi hasta hoy salieron con "servir junto" marcado, por si hay que revisar alguno.',
 'Nesto', 1, 'sa', GETDATE()),

('1.10.22.0', '2026-08-21', 'Corregido',
 'Comisiones: al marcar "incluir albaranes" o "incluir picking" sin vendedor',
 'Si se marcaba cualquiera de las dos casillas antes de haber seleccionado vendedor, no pasaba nada: la pantalla se quedaba igual y el error no se veia por ningun lado. Ahora no falla, y si algo va mal se avisa en pantalla.',
 'Nesto', 1, 'sa', GETDATE()),

('1.10.22.0', '2026-08-21', 'Corregido',
 'Alta de clientes: el nombre de una S.L. cuando Hacienda no responde',
 'Al dar de alta una empresa (CIF que empieza por letra) el nombre lo trae el censo de Hacienda. Si el certificado de la AEAT no esta disponible, ahora el campo del nombre aparece para que lo escribas tu, con su aviso, en vez de dejar el cliente sin nombre.',
 'Nesto', 1, 'sa', GETDATE()),

-- ---------- NestoAPI ----------
('1.10.22.0', '2026-08-21', 'Corregido',
 'Envios: las observaciones largas impedian crear la etiqueta',
 'Si las observaciones del envio pasaban de 80 caracteres, el alta fallaba entera y no se podia tramitar. Ahora se recortan a lo que cabe y el envio sale adelante. La direccion, el telefono y el codigo postal NO se recortan: ahi es mejor que avise a que llegue mal el paquete.',
 'NestoAPI', 1, 'sa', GETDATE()),

('1.10.22.0', '2026-08-21', 'Corregido',
 'El correo de "Informe de Devolucion de Producto" saltaba sin devoluciones',
 'Lo disparaba cualquier linea en negativo, asi que llegaba tambien por los descuentos (el "Suscribete y ahorra" de Amazon) y por los descuentos de portes. Ahora solo salta cuando se devuelve un producto de verdad. De paso, esos pedidos ya no exigen un comentario de motivo de devolucion para poder albaranarse.',
 'NestoAPI', 1, 'sa', GETDATE()),

('1.10.22.0', '2026-08-21', 'Corregido',
 'Aviso de "Financiacion a revisar" en pedidos ya autorizados',
 'Llegaba el aviso aunque el pedido llevara exactamente la forma de pago que el cliente tiene autorizada en su ficha para ese importe, que es algo ya revisado. Ahora solo avisa cuando la forma de pago no le corresponde al cliente para ese importe, o cuando el pedido se va a servir a trozos (sin "servir junto" ni "mantener junto") y por tanto se facturara en varias facturas mas pequenas.',
 'NestoAPI', 1, 'sa', GETDATE()),

('1.10.22.0', '2026-08-21', 'Mejorado',
 'Plazos de pago: el minimo por efecto pasa a 150 EUR',
 'Al elegir los plazos de un pedido, el minimo que debe quedar en cada efecto era de 100 EUR en la pantalla y de 150 EUR en el aviso que recibia administracion, asi que se marcaban como sospechosos plazos que la propia pantalla acababa de ofrecer. Ahora son 150 EUR en los dos sitios: es posible que en pedidos pequenos veas menos plazos disponibles que antes. Lo que el cliente tenga autorizado en su ficha se sigue respetando.',
 'NestoAPI', 1, 'sa', GETDATE()),

('1.10.22.0', '2026-08-21', 'Corregido',
 'Verifactu: rectificativas que no se llegaban a declarar',
 'Una factura rectificativa sin vinculaciones se quedaba sin declarar a Hacienda de forma indefinida, reintentandolo cada hora sin exito. Ya se genera la vinculacion y se declara sola.',
 'NestoAPI', 1, 'sa', GETDATE());
GO

SELECT Version, Categoria, Ambito, Titulo FROM Novedades WHERE Version = '1.10.22.0' ORDER BY Ambito, Id;
GO
