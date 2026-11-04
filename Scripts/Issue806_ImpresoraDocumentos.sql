-- NestoAPI#806 (sugerencia 973 de Alfredo, 04/11/26): la API imprime EN EL SERVIDOR (RDS2016) la factura/albarán en papel al
-- facturar por cualquier camino, con el interruptor Facturacion:ImprimirDocumentosEnServidor del Web.config. La impresora es
-- POR USUARIO: parámetro nuevo ImpresoraDocumentos = nombre de la impresora tal como está instalada en RDS2016 (el que sale
-- en «Dispositivos e impresoras», p. ej. el de la de Administración). La fila «(defecto)» es la de quien no tenga la suya; cada
-- usuario la cambia desde Nesto → Parámetros. La API crea la fila de cada usuario al leerla, copiando la de «(defecto)».
-- ¡¡CAMBIAR EL VALOR DE RELLENO!! Mientras sea el de relleno, cada documento en papel dará el aviso «no está instalada».
-- Carlos 04/11/26: el valor «(predeterminada)» vale para usar la impresora predeterminada de Windows en RDS2016 sin escribir su nombre.
-- Sin GRANT: ParametrosUsuario ya lo lee y escribe NestoAPI. Idempotente: se puede repetir sin miedo (no pisa un valor ya puesto).
-- Lanzar en la BD NV (NestoConnection) como sa.

-- Antes (con las otras impresoras de «(defecto)» como referencia)
SELECT Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación]
FROM ParametrosUsuario
WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave IN ('ImpresoraDocumentos', 'ImpresoraAgencia', 'ImpresoraAgenciaGLS', 'ImpresoraCodBarras');

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'ImpresoraDocumentos')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'ImpresoraDocumentos', '(defecto)', 'CAMBIAR - impresora de Administracion en RDS2016', 'Issue806', GETDATE());
END
GO

-- Después: la de «(defecto)» y los usuarios que ya tienen la suya
SELECT Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación]
FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'ImpresoraDocumentos'
ORDER BY CASE WHEN Usuario = '(defecto)' THEN 0 ELSE 1 END, Usuario;

-- Para poner la de verdad (cambiar el nombre):
-- UPDATE ParametrosUsuario SET Valor = 'NOMBRE EXACTO EN RDS2016', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'ImpresoraDocumentos';
