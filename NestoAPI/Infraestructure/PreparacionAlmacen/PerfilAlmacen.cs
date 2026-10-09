using NestoAPI.Models;
using System;
using System.Linq;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// GET api/Almacen/Perfil (Ariadna para tiendas, Carlos 09/10/26): a qué almacén pertenece quien ha entrado y qué
    /// pantallas le enseña Ariadna. En una tienda (Reina, Alcobendas) solo Salidas y Entradas y, dentro, solo las
    /// reposiciones de SU tienda; en Algete, todo como hasta ahora.
    /// </summary>
    public class PerfilAlmacenDTO
    {
        /// <summary>El usuario del token, sin dominio.</summary>
        public string Usuario { get; set; }
        /// <summary>Su AlmacénPedidoVta (ALG, REI, ALC…): el almacén donde trabaja. ALG si no tiene ninguno.</summary>
        public string Almacen { get; set; }
        /// <summary>El nombre del almacén para enseñarlo («Reina», Almacenes.Descripción). Null si no se sabe.</summary>
        public string NombreAlmacen { get; set; }
        /// <summary>«Tienda» o «Almacen».</summary>
        public string Perfil { get; set; }
        /// <summary>Lo mismo que Perfil = «Tienda», para no comparar textos en los clientes.</summary>
        public bool EsTienda { get; set; }
    }

    /// <summary>
    /// La regla del perfil de Ariadna, pura. El almacén de cada usuario es el mismo que ya usan Nesto y los permisos de las
    /// reposiciones (ParámetrosUsuario.AlmacénPedidoVta: Reina → REI, Paloma → ALC, los mozos → ALG): quien trabaja en una
    /// tienda es tienda. No hace falta ningún rol nuevo para decidirlo; los permisos de escritura los sigue poniendo cada
    /// endpoint (preparar y terminar la reposición de SU tienda, recibir en SU tienda).
    /// </summary>
    public static class ReglasPerfilAlmacen
    {
        public const string PERFIL_ALMACEN = "Almacen";
        public const string PERFIL_TIENDA = "Tienda";

        /// <summary>Las tiendas que preparan y reciben reposiciones (las mismas que ServicioPreparacionReposicion, sin Algete).</summary>
        internal static readonly string[] TIENDAS = { Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS };

        public static PerfilAlmacenDTO Deducir(string usuario, string almacenDelUsuario, string nombreAlmacen = null)
        {
            string almacen = string.IsNullOrWhiteSpace(almacenDelUsuario)
                ? Constantes.Almacenes.ALGETE
                : almacenDelUsuario.Trim().ToUpperInvariant();
            bool esTienda = TIENDAS.Contains(almacen);
            return new PerfilAlmacenDTO
            {
                Usuario = SinDominio(usuario),
                Almacen = almacen,
                NombreAlmacen = string.IsNullOrWhiteSpace(nombreAlmacen) ? null : nombreAlmacen.Trim(),
                Perfil = esTienda ? PERFIL_TIENDA : PERFIL_ALMACEN,
                EsTienda = esTienda
            };
        }

        internal static string SinDominio(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return null;
            }
            string limpio = usuario.Trim();
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 ? limpio.Substring(barra + 1) : limpio;
        }
    }
}
