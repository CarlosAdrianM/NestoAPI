-- NestoAPI#231 — Carga inicial del mapeo terminal TPV -> usuario en configuración.
--
-- OPCIONAL: la aplicación ya funciona sin esto (ContabilidadService usa un diccionario por
-- defecto como fallback, con el terminal de Paloma ya corregido). Ejecutar este script habilita
-- editar el mapeo EN CALIENTE (altas/bajas/cambios de terminal) sin recompilar ni publicar:
-- basta con actualizar el Valor (JSON) de este parámetro.
--
-- Parámetro: TerminalesUsuariosTPV, a nivel de empresa por defecto ('1') y usuario '(defecto)'.

IF NOT EXISTS (
    SELECT 1 FROM ParametrosUsuario
    WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'TerminalesUsuariosTPV'
)
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, Fecha_Modificación)
    VALUES (
        '1',
        'TerminalesUsuariosTPV',
        '(defecto)',
        '{' +
            '"91901505888":"Paloma",' +
            '"91900804275":"Victoria",' +
            '"26617120788":"Laura Camacho",' +
            '"00346609775":"Patricia",' +
            '"00132951570":"Web",' +
            '"00232951570":"Paygold",' +
            '"00025537534":"Pilar",' +
            '"51570001329":"Bizum tienda online",' +
            '"51570002329":"Bizum Paygold",' +
            '"00022126270":"Almacén",' +
            '"91901357047":"Almacén"' +
        '}',
        'NestoAPI',
        GETDATE()
    );
END
