-- 28/09/26: desbloquear la facturación de rutas (926037, 926373, 927014, 927146). Error: «No puede cambiar el campo
-- recoger si la linea tiene picking». Son regalos/expositores (base 0) marcados enteros para recoger («en carpeta») y
-- con picking. prdCrearAlbaránVta, regla del 07/04/22, los devuelve a pendiente con Recoger = 0 («las líneas de base
-- 0 no se meten en carpeta»), pero el trigger no deja cambiar Recoger con picking. Aquí se hace lo mismo que el SP
-- quitando también el picking de ESA línea (no lleva nada en esta entrega: Recoger = Cantidad; sin ubicaciones).
-- Ejecutar como sa en NV. Solo toca las líneas si siguen exactamente así.
USE NV;
GO
UPDATE LinPedidoVta SET Estado = -1, Recoger = 0, Picking = 0
WHERE Empresa = '1' AND [Nº Orden] IN (327935500, 328091200, 328410600, 328462700)
  AND Estado = 1 AND Recoger <> 0 AND Recoger = Cantidad AND [Base Imponible] = 0 AND Picking > 0;
SELECT @@ROWCOUNT AS LineasArregladas; -- deben ser 4
GO
