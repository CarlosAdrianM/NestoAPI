using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#879 (novedad 1320, Carlos 19/11/26): la «dirección completa» que se ENSEÑA de un contacto = Dirección + «, » + Dirección 2
    /// (#874). Es para pantalla (ficha, selector de clientes, cabecera del pedido): no se trunca. NO es la de la etiqueta de la agencia
    /// (<see cref="Agencias.DireccionEnvioCliente.Componer"/>, con su tope de 80), que no cambia. Lo que se guarda tampoco cambia.
    /// </summary>
    public static class DireccionCompletaCliente
    {
        internal const string SEPARADOR = ", ";

        /// <summary>LA REGLA (pura): sin Dirección 2, la Dirección tal cual (recortada); sin Dirección, la Dirección 2.</summary>
        public static string Componer(string direccion, string direccion2)
        {
            string calle = direccion?.Trim();
            string d2 = direccion2?.Trim();
            if (string.IsNullOrEmpty(d2))
            {
                return calle;
            }
            return string.IsNullOrEmpty(calle) ? d2 : calle + SEPARADOR + d2;
        }

        /// <summary>Longitud de CabFacturaVta.DireccionFiscal (nvarchar(200) en el EDMX).</summary>
        public const int LONGITUD_DIRECCION_FISCAL = 200;

        /// <summary>
        /// NestoAPI#874 (bloque 4a, Carlos 19/11/26): la dirección FISCAL (la que se congela en una factura NUEVA, la del 347 y la del
        /// mandato SEPA). Regla explícita, sin truncar a ciegas: la completa (<see cref="Componer"/>) si cabe ENTERA en
        /// <paramref name="maximo"/>; si no, la Dirección sola (como antes). Pura.
        /// </summary>
        public static string ParaFiscal(string direccion, string direccion2, int maximo = LONGITUD_DIRECCION_FISCAL)
        {
            string completa = Componer(direccion, direccion2);
            return completa != null && completa.Length <= maximo ? completa : direccion?.Trim();
        }

        /// <summary>
        /// Pone <c>direccionCompleta</c> a cada contacto de la lista con las filas de <see cref="Direccion2ClienteSql.LeerDeClientes"/>
        /// (null = sin columnas o la lectura falló: la Dirección tal cual). Pura.
        /// </summary>
        internal static void Rellenar<T>(IEnumerable<T> contactos, IReadOnlyDictionary<string, Direccion2Contacto> filas,
            Func<T, string> cliente, Func<T, string> contacto, Func<T, string> direccion, Action<T, string> poner)
        {
            if (contactos == null)
            {
                return;
            }
            foreach (T c in contactos.Where(c => c != null))
            {
                string d2 = null;
                if (filas != null && filas.TryGetValue(Direccion2ClienteSql.ClaveContacto(cliente(c), contacto(c)), out Direccion2Contacto fila))
                {
                    d2 = fila?.Direccion2;
                }
                poner(c, Componer(direccion(c), d2));
            }
        }

        /// <summary>
        /// La lista de clientes de una búsqueda (puede mezclar empresas en <c>GET api/Clientes?filtro=</c>): UNA consulta por empresa
        /// (en lotes, <see cref="Direccion2ClienteSql.LeerDeClientes"/>), nunca una por fila. Nunca lanza.
        /// </summary>
        public static void Rellenar(NVEntities db, IList<ClienteDTO> clientes)
            => RellenarPorEmpresa(db, clientes, c => c.empresa, c => c.cliente, c => c.contacto, c => c.direccion, (c, d) => c.direccionCompleta = d);

        /// <summary>
        /// NestoAPI#874: cualquier lista (pedidos, inscritos, clientes nuevos…) que lleve empresa, cliente, contacto y Dirección: UNA
        /// consulta por empresa (en lotes), nunca una por fila. Sin columnas o si falla, la Dirección tal cual. Nunca lanza.
        /// </summary>
        public static void RellenarPorEmpresa<T>(NVEntities db, IEnumerable<T> filas, Func<T, string> empresa, Func<T, string> cliente,
            Func<T, string> contacto, Func<T, string> direccion, Action<T, string> poner)
        {
            if (filas == null)
            {
                return;
            }
            foreach (IGrouping<string, T> porEmpresa in filas.Where(f => f != null).GroupBy(f => empresa(f)?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO))
            {
                Dictionary<string, Direccion2Contacto> direcciones2 = Direccion2ClienteSql.LeerDeClientes(db, porEmpresa.Key,
                    porEmpresa.Select(cliente), Direccion2ClienteSql.PARA_PANTALLA);
                Rellenar(porEmpresa, direcciones2, cliente, contacto, direccion, poner);
            }
        }

        /// <summary>Las direcciones de entrega de UN cliente (selector de entrega, plantilla): una consulta. Nunca lanza.</summary>
        public static void Rellenar(NVEntities db, string empresa, string cliente, IList<DireccionesEntregaClienteDTO> direcciones)
        {
            if (direcciones == null || direcciones.Count == 0)
            {
                return;
            }
            Dictionary<string, Direccion2Contacto> filas = Direccion2ClienteSql.LeerDeClientes(db, empresa, new[] { cliente }, Direccion2ClienteSql.PARA_PANTALLA);
            Rellenar(direcciones, filas, d => cliente, d => d.contacto, d => d.direccion, (d, valor) => d.direccionCompleta = valor);
        }

        /// <summary>Un solo contacto (ficha, cliente elegido en el selector): la Dirección 2 de ese contacto. Nunca lanza.</summary>
        public static void Rellenar(NVEntities db, ClienteDTO cliente)
        {
            if (cliente == null)
            {
                return;
            }
            cliente.direccionCompleta = Componer(cliente.direccion, Direccion2ClienteSql.Leer(db, cliente.empresa, cliente.cliente, cliente.contacto));
        }
    }
}
