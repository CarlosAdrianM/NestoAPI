/*
    NestoAPI#421 - PILOTO CON UN SOLO PRODUCTO, antes de soltar el lote de 231.

    Qué se prueba aquí y por qué:

    Quitar las categorías a mano en PrestaShop (como se hizo con el 38178) demuestra que el
    DESTINO es el bueno: producto marcado y fuera de las categorías ocultas, se ve en la tienda
    sin precio y sin botón de comprar.

    Lo que NO demuestra es el MECANISMO: que al mandarle por el bus la lista de secundarias más
    corta, el módulo de PrestaShop RETIRE por su cuenta las que sobran. El contrato de #414 dice
    que sí (lista vacía = retirar las que sobren), pero eso es código suyo.

    Si no las retirase, el lote entero no haría nada: los 231 seguirían ocultos, pareciendo que
    ha funcionado, y con las filas ya borradas en Nesto. De ahí este paso.

    Producto elegido: 38171 - ASR SKIN REJUVENATING COCKTAIL (5und)
      - Estado 0 (DISPONIBLE) y PVP 95,80: está vivo y se publica
      - ExclusivoProfesional ya = 1
      - Una única secundaria, COS/EPA (Ampollas uso exclusivo profesional), que es el caso de
        206 de los 220: al borrarla se queda con la lista VACÍA, que es justo el camino a probar
      - Principal COS/116 (Tratamientos faciales): por ahí tiene que seguir siendo navegable
      - Es hermano del 38178, el que ya se tocó a mano
*/

SET NOCOUNT ON;
USE NV;

DECLARE @Producto varchar(15) = '38171';

-- =====================================================================================
-- PASO 0 - Copia de seguridad (la misma tabla que usa el script del lote; si ya existe
-- de una ejecución anterior, no se vuelve a crear).
-- =====================================================================================
IF OBJECT_ID('dbo.ProductosCategoriasSecundarias_Backup_20260828') IS NULL
BEGIN
    SELECT * INTO dbo.ProductosCategoriasSecundarias_Backup_20260828
    FROM dbo.ProductosCategoriasSecundarias;
END

-- =====================================================================================
-- PASO 1 - Foto del ANTES. Apuntar lo que sale, para comparar.
-- =====================================================================================
SELECT RTRIM(p.Número) AS Producto, RTRIM(p.Nombre) AS Nombre, p.Estado, p.PVP,
       p.ExclusivoProfesional AS Marcado, RTRIM(p.Grupo)+'/'+RTRIM(p.SubGrupo) AS Principal
FROM dbo.Productos p
WHERE p.Empresa = '1' AND RTRIM(p.Número) = @Producto;

SELECT Orden, RTRIM(Grupo)+'/'+RTRIM(SubGrupo) AS Secundaria
FROM dbo.ProductosCategoriasSecundarias
WHERE Empresa = '1' AND RTRIM(Número) = @Producto
ORDER BY Orden;

-- =====================================================================================
-- PASO 2 - Quitarle las categorías exclusivas y encolarlo para que se republique.
--
-- Hay que encolar a mano: el borrado es en otra tabla, así que ningún trigger de Productos
-- se entera. Y el UPDATE de ExclusivoProfesional tampoco encola, porque el bloque de
-- sincronización de trgProductosUpd no mira esa columna.
-- =====================================================================================
DELETE FROM dbo.ProductosCategoriasSecundarias
WHERE Empresa = '1' AND RTRIM(Número) = @Producto
  AND (SubGrupo LIKE 'ep%' OR SubGrupo = 'EXP');

SELECT @@ROWCOUNT AS AsignacionesBorradas;   -- esperado: 1

INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
SELECT 'Productos', @Producto, 'Piloto salida categorias EP', GETDATE()
WHERE NOT EXISTS (SELECT 1 FROM Nesto_sync ns
                  WHERE ns.Tabla = 'Productos' AND ns.ModificadoId = @Producto
                    AND ns.Sincronizado IS NULL);

SELECT @@ROWCOUNT AS Encolados;              -- esperado: 1

-- =====================================================================================
-- PASO 3 - Esperar la pasada (5 minutos). Que la cola se haya vaciado:
-- =====================================================================================
-- SELECT * FROM Nesto_sync WHERE Tabla='Productos' AND ModificadoId='38171' ORDER BY FechaModificacion DESC;
-- Cuando Sincronizado deje de ser NULL, el mensaje ha salido.

/*
    PASO 4 - COMPROBAR EN LA TIENDA (esto es el piloto de verdad)

    a) Que PrestaShop ha soltado la categoría EP por su cuenta, sin tocarla a mano.
       Es lo único que se está probando aquí. Si sigue dentro, PARAR y hablar con ellos:
       el módulo no retira categorías y el lote no serviría.

    b) Como anónimo (ventana de incógnito): el producto SE VE, sin precio y sin botón de
       comprar, con el aviso de crear cuenta.

    c) Con una cuenta profesional: se ve el precio y se puede comprar con normalidad.

    d) Que sigue apareciendo bajo su categoría principal, Tratamientos faciales.

    Si a) y b) salen bien, están probadas las dos cosas a la vez: que el API publica la marca
    y que el módulo retira categorías por el bus. Entonces ya se puede lanzar el lote con
    OneShot_20260828_SacarProductosDeCategoriasEP.sql.
*/

-- =====================================================================================
-- MARCHA ATRÁS de este producto, si hiciera falta
-- =====================================================================================
/*
INSERT INTO dbo.ProductosCategoriasSecundarias (Empresa, Número, Orden, Grupo, SubGrupo, Usuario, [Fecha Modificación])
SELECT b.Empresa, b.Número, b.Orden, b.Grupo, b.SubGrupo, b.Usuario, b.[Fecha Modificación]
FROM dbo.ProductosCategoriasSecundarias_Backup_20260828 b
WHERE b.Empresa = '1' AND RTRIM(b.Número) = '38171'
  AND NOT EXISTS (SELECT 1 FROM dbo.ProductosCategoriasSecundarias a
                  WHERE a.Empresa = b.Empresa AND a.Número = b.Número AND a.Orden = b.Orden);

INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
VALUES ('Productos', '38171', 'Marcha atras piloto', GETDATE());
*/
