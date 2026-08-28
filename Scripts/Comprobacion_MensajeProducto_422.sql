/*
    NestoAPI#422 — comprobar que el refactor del constructor del ProductoDTO no cambio el mensaje.

    Esto NO es SQL: es el procedimiento, aqui para que no se pierda. El refactor promete "no
    cambia nada", asi que hay que poder demostrarlo comparando el mensaje ANTES y DESPUES.

    ANTES de desplegar (ya hecho el 28/08/2026, con el API anterior en produccion) se guardo la
    respuesta de estos cinco productos, elegidos por cubrir los casos distintos:

        16627   kit
        11183   con categorias secundarias
        27380   exclusivo profesional
        17404   normal
        38171   normal, sin secundarias (el del piloto de las categorias EP)

    DESPUES de desplegar, volver a pedirlos y comparar. En Git Bash:

        for p in 16627 11183 27380 17404 38171; do
            curl -s "https://api.nuevavision.es/api/Productos/Publicar/$p" -o despues_$p.json
            diff <(python -m json.tool antes_$p.json) <(python -m json.tool despues_$p.json) \
                && echo "$p IGUAL" || echo "$p CAMBIA"
        done

    Lo unico que puede cambiar legitimamente son los stocks (se mueven solos) y los precios si
    alguien los toco. Cualquier otra diferencia hay que mirarla.

    OJO: ese endpoint PUBLICA el producto ademas de devolverlo. Es inofensivo (manda el mismo
    contenido), pero no conviene lanzarlo sobre medio catalogo por costumbre.
*/
SELECT 'Ver el comentario de arriba: este fichero es el procedimiento, no una consulta.' AS Nota;
