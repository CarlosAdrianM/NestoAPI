-- NestoAPI#693 (28/10/26, Carlos): parámetro de usuario AvisosPedidosEnRiesgo = qué avisos de pedidos en riesgo recibe cada uno.
-- Valores: Todo (campana y correo resumen, lo de siempre) | SoloCorreo | SoloCampana | Nada. Se cambia desde la ventana de
-- pedidos en riesgo de Nesto (desplegable «Avisos»: PUT api/PedidosVenta/PromesaEnRiesgo/Avisos). Sin fila, manda «(defecto)».
-- Lanzar como sa en NV. Idempotente.
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM dbo.ParametrosUsuario WHERE Empresa = '1' AND Clave = 'AvisosPedidosEnRiesgo' AND Usuario = '(defecto)')
    INSERT INTO dbo.ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'AvisosPedidosEnRiesgo', '(defecto)', 'Todo', 'NestoAPI#693', GETDATE());
-- Carlos y Manuel (28/10/26): «nos va a saturar tanto mensaje»: solo el correo resumen de las 9:00 y las 15:00. Lo cambian ellos en Nesto cuando quieran.
IF NOT EXISTS (SELECT 1 FROM dbo.ParametrosUsuario WHERE Empresa = '1' AND Clave = 'AvisosPedidosEnRiesgo' AND Usuario = 'Carlos')
    INSERT INTO dbo.ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'AvisosPedidosEnRiesgo', 'Carlos', 'SoloCorreo', 'NestoAPI#693', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.ParametrosUsuario WHERE Empresa = '1' AND Clave = 'AvisosPedidosEnRiesgo' AND Usuario = 'Manuel')
    INSERT INTO dbo.ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'AvisosPedidosEnRiesgo', 'Manuel', 'SoloCorreo', 'NestoAPI#693', GETDATE());
SELECT Usuario, Valor FROM dbo.ParametrosUsuario WHERE Empresa = '1' AND Clave = 'AvisosPedidosEnRiesgo';
