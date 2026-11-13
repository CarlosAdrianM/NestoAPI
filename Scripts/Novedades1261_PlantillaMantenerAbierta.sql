/*
    Novedades 1261 (Lidia, 13/11/26): la plantilla de venta se cierra al grabar el pedido y hay quien prefiere seguir con
    otro cliente sin tener que abrirla de nuevo. Carlos: opción POR USUARIO, apagada por defecto.

    Parámetro de usuario: ParametrosUsuario empresa '1', clave 'PlantillaMantenerAbierta'.
      '0' (o sin fila) = como siempre: al grabar se cierra la plantilla y se abre el pedido.
      '1'             = al grabar se abre además una plantilla nueva y limpia, lista para el siguiente pedido.
    Lo lee Nesto al grabar (sin caché): vale al momento, sin reiniciar.

    Este script solo crea la fila «(defecto)» a '0' (todo parámetro de usuario nuevo necesita la suya). Idempotente.
    Para encendérselo a un usuario (cambie el nombre):
      INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
      VALUES ('1', 'PlantillaMantenerAbierta', 'NUEVAVISION\Usuario', '1', 'Novedades1261', GETDATE());
*/

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'PlantillaMantenerAbierta' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'PlantillaMantenerAbierta', '(defecto)', '0', 'Novedades1261', GETDATE());
GO

SELECT RTRIM(Clave) AS Clave, RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'PlantillaMantenerAbierta';
GO
