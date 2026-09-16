-- NestoAPI#482 (16/09/26): modo de servicio con el que nacen los pedidos que no lo informan (NestoApp,
-- TNV, y Nesto hasta que el usuario toque el selector). Decisión de Carlos: SIEMPRE «tras reponer de
-- tiendas» (3), y no el ServirJunto de la ficha del cliente, que dejaba pedidos sin servir nunca por
-- una referencia agotada o anulada. La fila (defecto) es la válvula de escape: si alguna vez hace falta
-- otro modo para todos (o para un usuario concreto, con su propia fila), se cambia aquí sin tocar código.
--
-- Sin la fila, la API y Nesto usan el 3 igualmente (ModosServicio.POR_DEFECTO): el script es opcional y
-- puede ejecutarse antes o después del deploy. BD: NV. Sin GRANTs (INSERT de datos). Idempotente.

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'ModoServicioPorDefecto' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'ModoServicioPorDefecto', '(defecto)', '3', 'NestoAPI', GETDATE());
END
GO

-- VERIFICACIÓN (debe devolver 1 fila con Valor = '3'):
SELECT Empresa, Clave, Usuario, Valor FROM ParametrosUsuario
WHERE Clave = 'ModoServicioPorDefecto' AND Usuario = '(defecto)';
GO
