/*
    NestoAPI#631 (stock obsoleto, corte 3): motivos con los que Compras o Dirección puede silenciar una propuesta de stock.

    Decisión de Carlos (15/10/26): aún no se sabe qué motivos aparecerán, así que la lista se amplía sin publicar con el
    parámetro «MotivosSilencioPropuestas» de la fila «(defecto)» de la empresa 1. Hoy: Nuevo, Estacional, Aparato.

    - La API lee SOLO la fila «(defecto)» (no hay filas por usuario: cada uno tendría su propia lista).
    - Lista separada por comas (o punto y coma), en el orden en que la ofrece Nesto. Sin la fila, o con la fila vacía, la API
      usa los tres de hoy, así que el script se puede lanzar antes o después del deploy.
    - Cada motivo, como mucho 20 caracteres (PropuestasStock.MotivoSilencio y PropuestasStockDecisiones.MotivoSilencio son
      varchar(20) y no tienen CHECK de valores): uno más largo no se ofrece. El campo Valor es char(162): la lista entera
      tiene que caber ahí.
    - Quitar un motivo de la lista no toca los silencios ya guardados con él: siguen vigentes hasta que caducan; solo deja de
      poderse elegir para silencios nuevos.
    - Nesto lo lee con GET api/PropuestasStock/MotivosSilencio.

    Para añadir uno: UPDATE ParametrosUsuario SET Valor = 'Nuevo, Estacional, Aparato, Fin de serie', Usuario2 = 'Carlos',
    [Fecha Modificación] = GETDATE() WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'MotivosSilencioPropuestas';

    Sin GRANT: ParametrosUsuario ya lo lee NestoAPI. Idempotente.
*/

SET NOCOUNT ON;
USE NV;
GO

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'MotivosSilencioPropuestas' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'MotivosSilencioPropuestas', '(defecto)', 'Nuevo, Estacional, Aparato', 'NestoAPI', GETDATE());
END
GO

-- VERIFICACIÓN: una fila con 'Nuevo, Estacional, Aparato'
SELECT Empresa, Clave, Usuario, RTRIM(Valor) AS Valor FROM ParametrosUsuario
WHERE Clave = 'MotivosSilencioPropuestas';
GO
