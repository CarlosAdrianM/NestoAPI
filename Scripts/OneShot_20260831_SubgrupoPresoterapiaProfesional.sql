/*
    Alta del subgrupo «Presoterapia profesional» (APA/PRE) y asignacion como categoria
    SECUNDARIA a 16 referencias.  31/08/2026.

    LO CRITICO ES EL NOMBRE
    -----------------------
    NestoSync hace findCategoryByName() sobre category_lang.name ANTES de crear nada:

      - Si el nombre coincide caracter a caracter -> ADOPTA la categoria 40294 que ya existe en
        la tienda, con su URL, sus metadatos y sus 305 palabras de texto ya escritas.
      - Si no coincide -> crea una categoria nueva y VACIA, y se pierde todo ese trabajo.

    Por eso la descripcion se escribe UNA sola vez, en una variable, y el script comprueba
    despues que quedo exactamente asi. Ojo con las tres formas de estropearlo: un espacio de
    mas al principio, una mayuscula en "profesional", y el acento que NO lleva ninguna de las
    dos palabras.

    La columna es char(50), asi que en la BD queda con relleno. NO es un problema:
    ProductoDTO.CargarCategoriasSecundarias hace .Trim() antes de publicar, asi que por el bus
    viaja exactamente «Presoterapia profesional».

    COMPROBADO EL 31/08/2026 (todo verificado contra produccion antes de escribir esto)
    ----------------------------------------------------------------------------------
      - No existe ningun subgrupo con esa descripcion, ni ninguno que se le parezca
        (LIKE '%reso%' no devuelve nada): findCategoryByName no tendra con que confundirse.
      - El codigo PRE esta libre en TODOS los grupos.
      - Las 16 referencias existen y ninguna esta de baja (Estado >= 0).
      - Ninguna de las 16 tiene HOY ninguna categoria secundaria, asi que mandar solo
        «Presoterapia profesional» no pisa nada. (La lista que viaja es AUTORITATIVA: lo que se
        manda sustituye a las secundarias que gestiona el modulo.)

    POR QUE APA Y NO COS (decision de Carlos, 31/08/2026)
    ----------------------------------------------------
    Las presoterapias son APARATOS. El grupo solo se usa como categoria padre si hubiera que
    crear la categoria de cero; como se va a adoptar la 40294 que ya existe, en la practica no
    se usa, pero deja la estructura de Nesto coherente.

    El codigo es de LETRAS (PRE) y no el 011 que tocaria por secuencia. En APA los codigos
    numericos (001-010) son la taxonomia principal —donde cuelga la ficha del producto— y los
    de letras (APA, EXP) son categorias comerciales. «Presoterapia profesional» es comercial y
    secundaria, como COS/OUT (Outlet Estetica) y COS/PRG (Pack Regalo), asi que va con letras.

    QUEDA FUERA A PROPOSITO
    -----------------------
    La referencia 44956 (PRESOTERAPIA OCULAR RELAX GLASSES), pendiente de una revision de
    marcas registradas.

    UNA DISCREPANCIA CON LA PETICION, SIN CONSECUENCIA
    --------------------------------------------------
    La peticion daba como principal de la 40738 (PANTALON PRESOTERAPIA LIBIS) «Aparatologia
    corporal», pero en Nesto es APA/010 «Recambios de Aparatologia». No cambia nada -la
    principal no se toca en ningun caso-, pero conviene saberlo por si a alguien le importa.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;
GO

DECLARE @Descripcion char(50) = 'Presoterapia profesional';   -- <<< LA CADENA CRITICA
DECLARE @Grupo char(3) = 'APA';
DECLARE @Subgrupo char(3) = 'PRE';
DECLARE @Usuario varchar(30) = SUSER_SNAME();

-- =============================================================================
-- 1) COMPROBACIONES ANTES DE TOCAR NADA
-- =============================================================================
PRINT '--- A) No debe existir ya ese subgrupo, ni nada parecido (esperado: 0 filas) ---';
SELECT RTRIM(Grupo) AS Grupo, RTRIM(Número) AS Codigo, '[' + RTRIM(Descripción) + ']' AS Descripcion
FROM SubGruposProducto
WHERE Empresa = '1'
  AND (LTRIM(RTRIM(Descripción)) = LTRIM(RTRIM(@Descripcion)) OR Descripción LIKE '%reso%');

PRINT '--- B) El codigo APA/PRE debe estar libre (esperado: 0 filas) ---';
SELECT RTRIM(Grupo) AS Grupo, RTRIM(Número) AS Codigo FROM SubGruposProducto
WHERE Empresa = '1' AND Grupo = @Grupo AND Número = @Subgrupo;

PRINT '--- C) Las 16 referencias: deben salir las 16, ninguna con Estado negativo ---';
SELECT r.R AS Referencia,
       CASE WHEN p.Número IS NULL THEN '*** NO EXISTE ***' ELSE RTRIM(p.Nombre) END AS Nombre,
       p.Estado,
       RTRIM(p.Grupo) + '/' + RTRIM(p.SubGrupo) AS PrincipalActual,
       (SELECT COUNT(*) FROM ProductosCategoriasSecundarias s
        WHERE s.Empresa = '1' AND s.Número = p.Número) AS SecundariasHoy
FROM (VALUES ('43310'),('36042'),('40967'),('40738'),('40695'),('32819'),('37994'),('32331'),
             ('34345'),('34346'),('41836'),('39975'),('39976'),('35777'),('32334'),('25539')) r(R)
LEFT JOIN Productos p ON p.Empresa = '1' AND p.Número = r.R
ORDER BY r.R;
--  SecundariasHoy debe ser 0 en las 16. Si alguna trae ya una categoria secundaria, PARAR:
--  la lista que viaja es autoritativa y este script la dejaria como unica, borrando la otra.

GO
--  ^^ Este GO es necesario: cierra el lote de las comprobaciones y sus DECLARE. Sin el, al
--  descomentar el bloque de abajo -que vuelve a declarar las mismas variables, para que se pueda
--  ejecutar suelto- SQL Server responderia "La variable @Descripcion ya se declaro".

/*  Revisadas las tres salidas, quitar el comentario del bloque y ejecutarlo entero:

BEGIN TRAN;

    DECLARE @Descripcion char(50) = 'Presoterapia profesional';
    DECLARE @Grupo char(3) = 'APA';
    DECLARE @Subgrupo char(3) = 'PRE';
    DECLARE @Usuario varchar(30) = SUSER_SNAME();

    -- 2) EL SUBGRUPO
    IF NOT EXISTS (SELECT 1 FROM SubGruposProducto
                   WHERE Empresa = '1' AND Grupo = @Grupo AND Número = @Subgrupo)
    BEGIN
        INSERT INTO SubGruposProducto (Empresa, Grupo, Número, Descripción, Usuario, [Fecha Modificación])
        VALUES ('1', @Grupo, @Subgrupo, @Descripcion, @Usuario, GETDATE());
        PRINT 'Subgrupo APA/PRE creado.';
    END

    -- 3) LAS 16 ASIGNACIONES, con Orden 1 (ninguna tiene otra secundaria)
    INSERT INTO ProductosCategoriasSecundarias (Empresa, Número, Orden, Grupo, SubGrupo, Usuario, [Fecha Modificación])
    SELECT '1', p.Número, 1, @Grupo, @Subgrupo, @Usuario, GETDATE()
    FROM Productos p
    WHERE p.Empresa = '1'
      AND RTRIM(p.Número) IN ('43310','36042','40967','40738','40695','32819','37994','32331',
                              '34345','34346','41836','39975','39976','35777','32334','25539')
      AND NOT EXISTS (SELECT 1 FROM ProductosCategoriasSecundarias s
                      WHERE s.Empresa = '1' AND s.Número = p.Número
                        AND s.Grupo = @Grupo AND s.SubGrupo = @Subgrupo);

    SELECT @@ROWCOUNT AS Asignadas;   -- debe dar 16

    -- 4) ENCOLAR PARA QUE LA TIENDA SE ENTERE
    --    Sin esto no pasa nada hasta que alguien toque cada ficha: el mensaje de Productos lleva
    --    las categorias secundarias, pero nada las republica solo. El job de los 5 minutos las
    --    drena en cuanto se encolan.
    INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
    SELECT 'Productos', RTRIM(p.Número), 'Alta Presoterapia profesional', GETDATE()
    FROM Productos p
    WHERE p.Empresa = '1'
      AND RTRIM(p.Número) IN ('43310','36042','40967','40738','40695','32819','37994','32331',
                              '34345','34346','41836','39975','39976','35777','32334','25539')
      AND NOT EXISTS (SELECT 1 FROM Nesto_sync ns
                      WHERE ns.Tabla = 'Productos' AND ns.ModificadoId = RTRIM(p.Número)
                        AND ns.Sincronizado IS NULL);

    SELECT @@ROWCOUNT AS Encoladas;   -- 16, o menos si alguna ya estaba pendiente

-- COMMIT TRAN;    <-- si Asignadas = 16
-- ROLLBACK TRAN;  <-- si no

*/

-- =============================================================================
-- 5) DESPUES: la comprobacion que de verdad importa
-- =============================================================================
PRINT '--- D) El nombre, entre corchetes y con su longitud: TIENE que ser [Presoterapia profesional] y 24 ---';
SELECT '[' + RTRIM(Descripción) + ']' AS Descripcion,
       LEN(RTRIM(Descripción)) AS Longitud,
       CASE WHEN RTRIM(Descripción) COLLATE Latin1_General_CS_AS = 'Presoterapia profesional' COLLATE Latin1_General_CS_AS
            THEN 'OK - coincide caracter a caracter'
            ELSE '*** NO COINCIDE: la tienda crearia una categoria vacia ***' END AS Veredicto
FROM SubGruposProducto
WHERE Empresa = '1' AND Grupo = 'APA' AND Número = 'PRE';
--  La comparacion va con collation CASE SENSITIVE a proposito: la de la base de datos no
--  distingue mayusculas, pero findCategoryByName de PrestaShop si, y es quien manda aqui.

PRINT '--- E) Las 16 asignaciones ---';
SELECT RTRIM(s.Número) AS Producto, RTRIM(p.Nombre) AS Nombre,
       RTRIM(s.Grupo) + '/' + RTRIM(s.SubGrupo) AS Secundaria, s.Orden
FROM ProductosCategoriasSecundarias s
INNER JOIN Productos p ON p.Empresa = s.Empresa AND p.Número = s.Número
WHERE s.Empresa = '1' AND s.Grupo = 'APA' AND s.SubGrupo = 'PRE'
ORDER BY s.Número;

PRINT '--- F) Seguimiento del encolado (repetir hasta que Pendientes sea 0) ---';
SELECT SUM(CASE WHEN Sincronizado IS NULL THEN 1 ELSE 0 END) AS Pendientes,
       SUM(CASE WHEN Sincronizado IS NOT NULL THEN 1 ELSE 0 END) AS YaPublicadas
FROM Nesto_sync
WHERE Tabla = 'Productos' AND Usuario = 'Alta Presoterapia profesional';

/*
    QUE COMPROBAR EN LA TIENDA, cuando Pendientes llegue a 0
    -------------------------------------------------------
    Que los 16 productos aparecen en la categoria 40294 QUE YA EXISTIA (con su texto y su URL),
    y NO en una categoria nueva y vacia con el mismo nombre. Si aparece una categoria duplicada,
    el nombre no coincidio: parar, avisar al equipo de PrestaShop y NO seguir asignando.

    MARCHA ATRAS
    ------------
    DELETE FROM ProductosCategoriasSecundarias WHERE Empresa='1' AND Grupo='APA' AND SubGrupo='PRE';
    DELETE FROM SubGruposProducto WHERE Empresa='1' AND Grupo='APA' AND Número='PRE';

    Y DESPUES reencolar los 16 en Nesto_sync: si no, la tienda se queda con la categoria puesta
    (el mismo problema del borrado a mano de las rebajas — quitar una fila no dispara nada).
*/
