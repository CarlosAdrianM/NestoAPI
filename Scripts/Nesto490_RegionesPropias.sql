-- Nesto#490 (4C.4, tramo 3, 14/10/26): piloto de la región propia de Nesto (sin Prism.Regions).
-- Nesto lee RegionesPropias del usuario al crear la ventana principal: con '1' las pestañas de MainRegion y las
-- regiones de PedidoVentaView, PedidoCompraView y ListaRapportsView van por la región propia (GestorRegiones +
-- ServicioNavegacionNesto); con cualquier otro valor, sin fila o si no se puede leer, por Prism como siempre.
-- Se lee una vez por arranque: para cambiar de una a otra hay que cerrar y volver a abrir Nesto.
-- Fila (defecto) = '0' para que el parámetro exista para todos, y se activa usuario a usuario con su propia fila.
-- Cuando el piloto lleve semanas sin errores: dar la región propia a todos, retirar el parámetro y el puente con Prism.
--
-- BD: NV. Sin GRANTs (INSERT de datos). Idempotente. Ejecutar antes o después del deploy (sin fila → Prism).

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'RegionesPropias' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'RegionesPropias', '(defecto)', '0', 'NestoAPI', GETDATE());
END
GO

-- Piloto (Carlos, 14/10/26): solo él con la región propia; usuario como lo guardan los demás parámetros («Carlos», sin dominio).
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'RegionesPropias' AND Usuario = 'Carlos')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'RegionesPropias', 'Carlos', '1', 'NestoAPI', GETDATE());
GO

-- VERIFICACIÓN:
SELECT Empresa, Clave, Usuario, Valor FROM ParametrosUsuario WHERE Clave = 'RegionesPropias';
GO
