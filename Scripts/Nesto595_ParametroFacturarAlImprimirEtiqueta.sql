-- NestoAPI#595 (slice 5, Ariadna#13): la etiqueta de agencia desde el packing de Ariadna
-- (POST api/Almacen/Pedidos/{pedido}/Etiqueta) factura el pedido al imprimir, por el mismo camino que
-- «Facturar al imprimir etiqueta» de Agencias en Nesto (#592, FacturacionRutas/FacturarPedido), SOLO si el
-- usuario tiene este parametro a '1'. En Nesto es una casilla de la ventana; en Ariadna no hay casilla.
--
-- Protocolo de siempre:
--   1. El usuario va SIN dominio ('Andre', no 'NUEVAVISION\Andre').
--   2. Hace falta la fila '(defecto)' con el valor SEGURO ('0') para que el parametro exista y se propague
--      (LeerParametro copia la del defecto al usuario la primera vez).
--
-- Nace APAGADO para todos. Solo '1', 'true' o 'si' (recortado, sin distinguir mayusculas) factura; cualquier
-- otro valor, la ausencia de fila o un fallo al leerlo NO facturan.

SET NOCOUNT ON;
USE NV;

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario
               WHERE Empresa = '1' AND Clave = 'FacturarAlImprimirEtiqueta' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'FacturarAlImprimirEtiqueta', '(defecto)', '0', SYSTEM_USER, GETDATE());
END

SELECT RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor, [Fecha Modificación] AS FMod
FROM ParametrosUsuario WHERE Clave = 'FacturarAlImprimirEtiqueta' ORDER BY Usuario;

/*
    ENCENDERLO PARA UN MOZO (con la API ya publicada; se lee en cada etiqueta, no hace falta reiniciar):

    IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'FacturarAlImprimirEtiqueta' AND Usuario = 'Andre')
        INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
        VALUES ('1', 'FacturarAlImprimirEtiqueta', 'Andre', '1', SYSTEM_USER, GETDATE());
    ELSE
        UPDATE ParametrosUsuario SET Valor = '1', Usuario2 = SYSTEM_USER, [Fecha Modificación] = GETDATE()
        WHERE Empresa = '1' AND Clave = 'FacturarAlImprimirEtiqueta' AND Usuario = 'Andre';

    APAGARLO: el mismo UPDATE con Valor = '0'.
    Ojo: facturar pide el grupo/rol Almacén, Tiendas, Administración o Dirección (FacturarPedido); los mozos con
    el rol «Almacén» de Ariadna lo tienen. Ariadna NO imprime facturas ni albaranes: si el pedido lleva el
    documento en papel, la respuesta lo avisa para imprimirlo desde Nesto.
*/
