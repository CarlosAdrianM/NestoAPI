/*
    Épica #784 (incidencia 1186, Carlos 11/11/26): quién puede borrar filas del inventario (DELETE api/Inventarios/Filas/{n}).
    Dirección, Informática y Administración siempre; el resto, solo con ParametrosUsuario «InventarioBorrarFilas» = '1' (empresa 1).
    Crea la fila «(defecto)» = '0' y lo enciende para los encargados del almacén (Alfredo y Santiago; Carlos confirma la lista).
    Para dárselo a otro:
        INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
        VALUES ('1', 'InventarioBorrarFilas', 'Fulano', '1', 'Issue784', GETDATE());
    Idempotente. Se puede lanzar antes o después del deploy (sin la fila, nadie fuera de esos grupos puede borrar). Sin GRANT nuevo.
*/

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'InventarioBorrarFilas' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'InventarioBorrarFilas', '(defecto)', '0', 'Issue784', GETDATE());

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'InventarioBorrarFilas' AND Usuario = 'Alfredo')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'InventarioBorrarFilas', 'Alfredo', '1', 'Issue784', GETDATE());
ELSE
    UPDATE ParametrosUsuario SET Valor = '1', Usuario2 = 'Issue784', [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'InventarioBorrarFilas' AND Usuario = 'Alfredo' AND RTRIM(Valor) <> '1';

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'InventarioBorrarFilas' AND Usuario = 'Santiago')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'InventarioBorrarFilas', 'Santiago', '1', 'Issue784', GETDATE());
ELSE
    UPDATE ParametrosUsuario SET Valor = '1', Usuario2 = 'Issue784', [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'InventarioBorrarFilas' AND Usuario = 'Santiago' AND RTRIM(Valor) <> '1';
GO

SELECT RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'InventarioBorrarFilas';
GO
