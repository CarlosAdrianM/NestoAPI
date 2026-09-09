/*
    Clientes.Empleados + Clientes.EmpleadosFecha (NestoAPI#464)

    PARA QUÉ
    --------
    Campaña de contratación de los alumnos de los cursos del SEPE: hay que apuntar a los centros de
    la Comunidad de Madrid con más probabilidad de contratar, y para eso se les pregunta cuántos
    empleados tienen al meter el rapport (Nesto#469, NestoApp#170).

    COLUMNAS
    --------
    - Empleados tinyint NULL: 0 = sin empleados, 1..4 exactos, 5 = "5 o más". NULL = no se ha
      preguntado todavía.
    - EmpleadosFecha datetime NULL: cuándo se recogió o confirmó por última vez. Sirve para saber si
      el dato está viejo y para no volver a preguntarlo.

    Se rellena en la fila del cliente PRINCIPAL (ClientePrincipal = 1), que es la que representa al
    centro. La escribe NestoAPI al guardar el rapport; los clientes (Nesto, NestoApp) no tocan la
    tabla.

    CÓMO SE EJECUTA
    ---------------
    Es DDL: desde SSMS con `sa` (el login `nuevavision` no tiene ALTER). No hace falta ningún GRANT
    nuevo: los permisos son de tabla y la tabla ya los tiene.

    LO QUE VA CON ESTE SCRIPT (mismo commit)
    ----------------------------------------
    1. EDMX (NestoEntities.edmx) editado A MANO en los tres modelos (SSDL, CSDL, MSL) + las dos
       propiedades en Models/Cliente.cs. NO hacer "Update Model from Database" desde Visual Studio
       (es lo que en #413 dejó una propiedad fantasma). Los tests ClienteEmpleadosTests vigilan que
       el mapeo siga en los tres modelos.
    2. ClienteDTO: empleados, empleadosFecha y preguntarEmpleados (regla: código postal de Madrid,
       calculada SOLO en el servidor).
    3. SeguimientoClienteDTO.Empleados (opcional): al guardar el rapport, la API actualiza la ficha
       del cliente principal.

    Idempotente: si las columnas ya existen no hace nada.
*/

USE NV;
GO

SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Clientes') AND name = 'Empleados')
BEGIN
    PRINT 'La columna Clientes.Empleados ya existe: no se hace nada.';
END
ELSE
BEGIN
    ALTER TABLE dbo.Clientes ADD Empleados tinyint NULL;
    PRINT 'Añadida Clientes.Empleados (tinyint NULL).';
END

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Clientes') AND name = 'EmpleadosFecha')
BEGIN
    PRINT 'La columna Clientes.EmpleadosFecha ya existe: no se hace nada.';
END
ELSE
BEGIN
    ALTER TABLE dbo.Clientes ADD EmpleadosFecha datetime NULL;
    PRINT 'Añadida Clientes.EmpleadosFecha (datetime NULL).';
END
GO

-- Comprobación
SELECT name, TYPE_NAME(system_type_id) AS tipo, is_nullable
FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.Clientes') AND name IN ('Empleados', 'EmpleadosFecha');
