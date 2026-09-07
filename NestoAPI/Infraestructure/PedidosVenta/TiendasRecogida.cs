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

            /// <summary>Horario de recogida, tal cual se le enseña al cliente.</summary>
            public string Horario { get; set; }

            public string Telefono { get; set; }
        }

        private static readonly List<Tienda> TIENDAS = new List<Tienda>
        {
            new Tienda
            {
                Almacen = Constantes.Almacenes.ALGETE,
                Nombre = "Algete",
                Ruta = "AM",
                Direccion = "C/ Río Tiétar, 11 - Nave 22",
                CodigoPostal = "28110",
                Poblacion = "Algete (Madrid)",
                Telefono = "916281914",
                Horario = string.Empty
            },
            new Tienda
            {
                Almacen = Constantes.Almacenes.ALCOBENDAS,
                Nombre = "Alcobendas",
                Ruta = "ALC",
                Direccion = string.Empty,
                CodigoPostal = string.Empty,
                Poblacion = "Alcobendas (Madrid)",
                Telefono = string.Empty,
                Horario = string.Empty
            },
            new Tienda
            {
                Almacen = Constantes.Almacenes.REINA,
                Nombre = "Reina",
                Ruta = "REI",
                Direccion = string.Empty,
                CodigoPostal = string.Empty,
                Poblacion = "Madrid",
                Telefono = string.Empty,
                Horario = string.Empty
            }
        };

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
