using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    /// <summary>
    /// Sugerencia 1340 (decisiones 3, 4 y 7 de Carlos, 18/11/26): el modo 6, «Junto con lo pendiente del cliente», en el picking. Puro.
    /// <list type="bullet">
    /// <item>Un pedido en modo 6 sale en la MISMA pasada que todo lo pendiente con lo que se junta (<see cref="PedidoPicking.JuntoCon"/>):
    /// si alguno no está en la pasada o no sale en ella, el 6 espera (<see cref="MotivoNoSalePicking.EsperaPendienteDelCliente"/>). Como
    /// todos van en el mismo número de picking, el packing los mete en un solo envío por el camino de siempre (mismo cliente y dirección).</item>
    /// <item>Además el propio pedido sale entero (como «Todo junto»: <see cref="PedidoPicking.ExigeStockDeTodo"/>).</item>
    /// <item>Sin nada con lo que juntarse (se anuló, ya salió, ya no está pendiente) es un «Todo junto» más y se decide con los demás.</item>
    /// <item>Lo pendiente que sale solo en parte (un «según vaya entrando») cuenta como que sale: el 6 va con esa entrega.</item>
    /// <item>Portes: el grupo que viaja junto (el 6 que sale y lo del mismo cliente y contacto que sale con él) suma lo que se factura para el
    /// mínimo y paga portes UNA vez; lo que ya los lleva no se toca.</item>
    /// </list>
    /// </summary>
    public static class JuntoConPendientePicking
    {
        /// <summary>¿Es un 6 que espera a algo? (Los que no tienen nada con lo que juntarse se deciden como un «Todo junto» más.)</summary>
        public static bool EsperaAlgo(PedidoPicking pedido)
            => pedido != null
                && pedido.ModoServicioEfectivo == Constantes.Pedidos.ModosServicio.JUNTO_CON_LO_PENDIENTE
                && pedido.JuntoCon != null && pedido.JuntoCon.Any();

        /// <summary>
        /// Antes de decidir nada: cada 6 espera a lo que ni siquiera está en esta pasada (eso seguro que no sale). Lo que sí está se mira
        /// después, cuando se sepa si sale (<see cref="MarcarEsperasFinales"/>). Así los ensayos previos (muestras sin stock) ven al 6 como
        /// lo que puede llegar a ser.
        /// </summary>
        public static void MarcarEsperasIniciales(IEnumerable<PedidoPicking> candidatos)
        {
            List<PedidoPicking> lista = (candidatos ?? Enumerable.Empty<PedidoPicking>()).Where(c => c != null).ToList();
            foreach (PedidoPicking pedido in lista)
            {
                pedido.EsperandoA = EsperaAlgo(pedido)
                    ? pedido.JuntoCon.Where(j => !lista.Any(c => EsElMismo(c, pedido.Empresa, j.Pedido))).Select(j => j.Pedido).ToList()
                    : new List<int>();
            }
        }

        /// <summary>Ya decididos los demás: el 6 espera a lo que no está en la pasada o no sale en ella (Borrar).</summary>
        public static void MarcarEsperasFinales(PedidoPicking pedido, IEnumerable<PedidoPicking> candidatos)
        {
            if (!EsperaAlgo(pedido))
            {
                if (pedido != null)
                {
                    pedido.EsperandoA = new List<int>();
                }
                return;
            }
            List<PedidoPicking> lista = (candidatos ?? Enumerable.Empty<PedidoPicking>()).Where(c => c != null).ToList();
            pedido.EsperandoA = pedido.JuntoCon
                .Where(j => !lista.Any(c => EsElMismo(c, pedido.Empresa, j.Pedido) && !c.Borrar))
                .Select(j => j.Pedido)
                .ToList();
        }

        /// <summary>¿Este pedido puede viajar con un 6 de la pasada? (Sus portes se deciden con el grupo, al final.)</summary>
        public static bool PuedeIrConUnModo6(PedidoPicking pedido, IEnumerable<PedidoPicking> candidatos)
            => pedido != null && (candidatos ?? Enumerable.Empty<PedidoPicking>())
                .Any(c => c != null && c != pedido && EsperaAlgo(c) && (c.EsperandoA == null || !c.EsperandoA.Any())
                    && c.JuntoCon.Any(j => EsElMismo(pedido, c.Empresa, j.Pedido)));

        /// <summary>
        /// Los portes de los que se han dejado para el final (<paramref name="aplazados"/>, en orden). Los que viajan con un 6 que sale se
        /// suman (importe de todo el grupo para el mínimo) y el grupo paga portes una sola vez; los demás, como siempre, cada uno el suyo.
        /// </summary>
        /// <param name="generar">Pone los portes a un pedido (GeneradorPortes).</param>
        /// <param name="necesitaPortes">La regla de siempre de un pedido (PedidoPicking.hayQueSumarPortes); sustituible en los tests.</param>
        public static void PortesDeLosAplazados(IList<PedidoPicking> aplazados, IEnumerable<PedidoPicking> candidatos,
            Action<PedidoPicking> generar, Func<PedidoPicking, bool> necesitaPortes = null)
        {
            Func<PedidoPicking, bool> necesita = necesitaPortes ?? (p => p.hayQueSumarPortes());
            List<PedidoPicking> salen = (candidatos ?? Enumerable.Empty<PedidoPicking>()).Where(c => c != null && !c.Borrar).ToList();
            var gruposHechos = new HashSet<string>();
            foreach (PedidoPicking pedido in (aplazados ?? new List<PedidoPicking>()).Where(p => p != null && !p.Borrar))
            {
                List<PedidoPicking> grupo = Grupo(pedido, salen);
                if (grupo == null)
                {
                    if (necesita(pedido))
                    {
                        generar?.Invoke(pedido);
                    }
                    continue;
                }
                string clave = Clave(pedido);
                if (!gruposHechos.Add(clave))
                {
                    continue;
                }
                decimal total = grupo.Sum(Importe);
                bool yaHayPortes = grupo.Any(m => m.LlevaPortes());
                foreach (PedidoPicking miembro in grupo.Where(m => aplazados.Contains(m)).OrderBy(m => m.Id))
                {
                    miembro.ImporteEntregaJunto = total - Importe(miembro);
                    miembro.PortesYaEnElGrupo = yaHayPortes;
                    if (necesita(miembro))
                    {
                        generar?.Invoke(miembro);
                        yaHayPortes = true;
                    }
                }
            }
        }

        /// <summary>Lo que sale junto con el 6: los que salen del mismo cliente y contacto, si alguno es un 6 que sale. Null si no hay grupo.</summary>
        private static List<PedidoPicking> Grupo(PedidoPicking pedido, List<PedidoPicking> salen)
        {
            string clave = Clave(pedido);
            List<PedidoPicking> mismos = salen.Where(c => Clave(c) == clave).ToList();
            return mismos.Any(EsperaAlgo) && mismos.Count > 1 ? mismos : null;
        }

        private static decimal Importe(PedidoPicking pedido)
        {
            try
            {
                return new GestorImportesMinimos(pedido).ImporteEntrega();
            }
            catch
            {
                return 0;
            }
        }

        private static string Clave(PedidoPicking p)
            => $"{p.Empresa?.Trim()}|{p.Cliente?.Trim().ToUpperInvariant()}|{p.Contacto?.Trim().ToUpperInvariant()}";

        private static bool EsElMismo(PedidoPicking candidato, string empresa, int pedido)
            => candidato != null && candidato.Id == pedido && string.Equals(candidato.Empresa?.Trim(), empresa?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
