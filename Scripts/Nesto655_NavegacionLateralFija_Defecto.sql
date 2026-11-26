/*
    Nesto#655 (26/11/26): la chincheta del menú lateral guarda la preferencia en el parámetro de usuario NavegacionLateralFija.
    Sin su fila «(defecto)», PUT api/ParametrosUsuario da 404 (no hay fila que cambiar ni de la que copiar), como pasó con
    PlantillaBorradorActualizarCargado (incidencia 1481). Nace apagado («0»: el menú se pliega solo); cada usuario lo fija con la chincheta.
    Lanzar como sa en la BD NV. Se puede lanzar más de una vez.
*/
USE NV;
GO
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NavegacionLateralFija' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NavegacionLateralFija', '(defecto)', '0', 'Nesto#655', GETDATE());
GO
SELECT RTRIM(Clave) AS Clave, RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'NavegacionLateralFija';
GO
