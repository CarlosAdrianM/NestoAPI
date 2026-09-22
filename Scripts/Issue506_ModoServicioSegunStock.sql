-- NestoAPI#506: el modo de servicio por defecto pasa a decidirse por el stock real del pedido.
-- Contrato del parámetro ModoServicioPorDefecto: '0' = según el stock (nuevo defecto); 1..4 = modo forzado por el usuario.
-- Todas las filas actuales valen '3' porque el lector copia (defecto) a cada usuario al leerlo (16-22/09/2026):
-- no son excepciones explícitas, así que se pasan todas a '0'. Quien quiera un modo fijo lo pone después a mano.
-- Ejecutar en NV (NestoConnection) al publicar la API.
USE NV;
GO

UPDATE dbo.ParámetrosUsuario
SET Valor = '0', Usuario2 = 'NestoAPI#506', [Fecha Modificación] = GETDATE()
WHERE Clave = 'ModoServicioPorDefecto' AND LTRIM(RTRIM(Valor)) = '3';

-- Comprobación
SELECT RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor, Usuario2, [Fecha Modificación]
FROM dbo.ParámetrosUsuario WHERE Clave = 'ModoServicioPorDefecto' ORDER BY Usuario;
GO
