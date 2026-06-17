-- NestoAPI#231 — Tabla de mapeo terminal TPV -> usuario, editable sin recompilar.
--
-- La columna ParametrosUsuario.Valor es char(162) y no cabe el mapeo, así que se usa una tabla
-- dedicada. La aplicación funciona sin ella (ContabilidadService usa un diccionario por defecto
-- como fallback, con el terminal de Paloma ya corregido); ejecutar esto habilita editar el mapeo
-- (altas/bajas/cambios de terminal) con un simple UPDATE/INSERT, sin recompilar ni publicar.
--
-- GRANT al usuario de la conexión de negocio (cuenta máquina), ver patrón de NestoConnection.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TerminalesUsuariosTPV')
BEGIN
    CREATE TABLE TerminalesUsuariosTPV (
        Terminal varchar(20) NOT NULL CONSTRAINT PK_TerminalesUsuariosTPV PRIMARY KEY,
        Usuario  varchar(50) NOT NULL
    );

    INSERT INTO TerminalesUsuariosTPV (Terminal, Usuario) VALUES
        ('91901505888', 'Paloma'),               -- antes 91900804273 (dado de baja)
        ('91900804275', 'Victoria'),
        ('26617120788', 'Laura Camacho'),
        ('00346609775', 'Patricia'),
        ('00132951570', 'Web'),
        ('00232951570', 'Paygold'),
        ('00025537534', 'Pilar'),
        ('51570001329', 'Bizum tienda online'),
        ('51570002329', 'Bizum Paygold'),
        ('00022126270', 'Almacén'),
        ('91901357047', 'Almacén');

    GRANT SELECT ON TerminalesUsuariosTPV TO [NUEVAVISION\RDS2016$];
END
