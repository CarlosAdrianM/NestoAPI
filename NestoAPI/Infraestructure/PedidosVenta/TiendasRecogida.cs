using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// TNV#70: las tres tiendas donde un cliente puede pasar a recoger su pedido en vez de que se
    /// lo mandemos.
    ///
    /// <para><b>Lo que de verdad decide esto es el ALMACÉN.</b> El pedido tiene que prepararse en
    /// la tienda donde lo van a recoger, así que la tienda elegida pone el almacén de todas las
    /// líneas; hasta ahora los pedidos de la app salían siempre de Algete, fijo en el código. La
    /// ruta va detrás, y con ella desaparecen los portes solos: <c>esRutaConPortes</c> solo dice
    /// que sí para FW, 00, 16, AT y OT, y ninguna de estas tres lo es.</para>
    ///
    /// <para>El emparejamiento almacén-ruta no es una invención: es el que ya usan los pedidos de
    /// las tiendas (últimos 6 meses: ruta ALC con almacén ALC, ruta REI con almacén REI, ruta AM
    /// con almacén ALG).</para>
    ///
    /// <para>La lista vive aquí y no en la base de datos porque son tres y no cambian; la tabla
    /// <c>Almacenes</c> no guarda ni dirección ni horario, y meter una tabla nueva para tres filas
    /// que se tocan una vez cada diez años es más sitio donde equivocarse.</para>
    /// </summary>
    public static class TiendasRecogida
    {
        /// <summary>Una tienda donde se puede recoger.</summary>
        public class Tienda
        {
            /// <summary>El almacén en el que se prepara el pedido. Es lo que identifica la tienda.</summary>
            public string Almacen { get; set; }

            /// <summary>Cómo se llama para el cliente.</summary>
            public string Nombre { get; set; }

            /// <summary>La ruta del pedido. Ninguna de las tres lleva portes.</summary>
            public string Ruta { get; set; }

            public string Direccion { get; set; }
            public string CodigoPostal { get; set; }
            public string Poblacion { get; set; }

            /// <summary>
            /// La ficha de la tienda en Google. El horario NO se guarda aquí a propósito: en
            /// Google se mantiene al día y aquí se quedaría viejo sin que nadie se enterara, y un
            /// horario viejo manda al cliente a una tienda cerrada. Desde la app se abre esta
            /// ficha, que además le da el "cómo llegar".
            /// </summary>
            public string UrlGoogle { get; set; }

            public string Telefono { get; set; }
        }

        /// <summary>
        /// Direcciones y teléfonos, tal como los publica nuestra propia web (la página de
        /// contacto de la tienda online).
        ///
        /// <para><b>El horario no está aquí, y es deliberado</b>: el que mantenemos al día es el
        /// de Google, así que la app abre la ficha de Google de la tienda. Un horario copiado
        /// aquí envejecería sin que nadie se diera cuenta, y un horario viejo manda al cliente a
        /// una tienda cerrada.</para>
        /// </summary>
        private static readonly List<Tienda> TIENDAS = new List<Tienda>
        {
            new Tienda
            {
                Almacen = Constantes.Almacenes.ALGETE,
                Nombre = "Algete",
                Ruta = "AM",
                Direccion = "C/ Río Tiétar, 11 - Políg. Ind. Los Nogales",
                CodigoPostal = "28119",
                Poblacion = "Algete (Madrid)",
                Telefono = "916281914",
                UrlGoogle = UrlDeGoogle("Nueva Visión, C/ Río Tiétar 11, 28119 Algete, Madrid")
            },
            new Tienda
            {
                Almacen = Constantes.Almacenes.ALCOBENDAS,
                Nombre = "Alcobendas",
                Ruta = "ALC",
                Direccion = "C/ La Granja, 1 - Local 10, P.I. La Granja",
                CodigoPostal = "28108",
                Poblacion = "Alcobendas (Madrid)",
                Telefono = "916281914",
                UrlGoogle = UrlDeGoogle("Nueva Visión, C/ La Granja 1, 28108 Alcobendas, Madrid")
            },
            new Tienda
            {
                Almacen = Constantes.Almacenes.REINA,
                Nombre = "Madrid centro",
                Ruta = "REI",
                Direccion = "C/ Reina, 5 - Local",
                CodigoPostal = "28004",
                Poblacion = "Madrid",
                Telefono = "915311914",
                UrlGoogle = UrlDeGoogle("Nueva Visión, C/ Reina 5, 28004 Madrid")
            }
        };

        /// <summary>
        /// La ficha de la tienda en Google Maps, buscada por nombre y dirección. Se monta así, y
        /// no con un identificador de sitio, porque un identificador hay que ir a buscarlo y no
        /// se puede comprobar desde aquí que siga siendo el bueno; el nombre y la dirección son
        /// los mismos datos que ya enseñamos, así que si un día cambian, cambia también el
        /// enlace. Si algún día tenemos los place_id, se sustituye esto y ya.
        /// </summary>
        internal static string UrlDeGoogle(string nombreYDireccion)
        {
            return "https://www.google.com/maps/search/?api=1&query="
                + Uri.EscapeDataString(nombreYDireccion);
        }

        public static IReadOnlyList<Tienda> Todas => TIENDAS;

        /// <summary>
        /// La tienda con ese almacén, o null si no es ninguna de las tres. Es la puerta: el
        /// cliente manda un código en la petición y aquí se decide si vale, para que nadie pueda
        /// colocar el pedido en un almacén cualquiera.
        /// </summary>
        public static Tienda Buscar(string almacen)
        {
            if (string.IsNullOrWhiteSpace(almacen))
            {
                return null;
            }
            return TIENDAS.FirstOrDefault(t =>
                string.Equals(t.Almacen, almacen.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }
}
