/*
    Nesto#654 / incidencia 1481 (Lidia, 26/11/26): la casilla «Al guardar un borrador que he cargado, actualizarlo en vez de crear
    otro» de la plantilla da «No se ha podido guardar su preferencia» porque el parámetro de usuario no tiene su fila «(defecto)»
    (PUT api/ParametrosUsuario → 404: no hay fila que cambiar ni de la que copiar). Nace apagado para todos; cada usuario lo
    enciende con la casilla. Lanzar como sa en la BD NV. Se puede lanzar más de una vez.
*/
USE NV;
GO
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'PlantillaBorradorActualizarCargado' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'PlantillaBorradorActualizarCargado', '(defecto)', '0', 'Nesto#654', GETDATE());
GO
SELECT RTRIM(Clave) AS Clave, RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'PlantillaBorradorActualizarCargado';
GO
