using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Clientes.CambioFiscal
{
    /// <summary>NestoAPI#876: una ficha (contacto) de un cliente, con lo que se puede copiar.</summary>
    public class FichaCambioFiscal
    {
        public string Contacto { get; set; }
        public bool ClientePrincipal { get; set; }
        public string Nombre { get; set; }
        public short? Estado { get; set; }
        public string Vendedor { get; set; }
        public string Iva { get; set; }
        public string Comentarios { get; set; }
        public string DiasEnServir { get; set; }
        public string Ruta { get; set; }
        public string PeriodoFacturacion { get; set; }
        public decimal NoComisiona { get; set; }
        public bool MantenerJunto { get; set; }
        public string Ccc { get; set; }
    }

    public class VendedorGrupoCambioFiscal
    {
        public string Contacto { get; set; }
        public string GrupoProducto { get; set; }
        public string Vendedor { get; set; }
        public short Estado { get; set; }
    }

    public class CondPagoCambioFiscal
    {
        public string Contacto { get; set; }
        public string FormaPago { get; set; }
        public string PlazosPago { get; set; }
        public decimal ImporteMinimo { get; set; }
        public bool CccObligatorio { get; set; }

        internal string Clave => $"{T(FormaPago)}|{T(PlazosPago)}|{Math.Round(ImporteMinimo, 2)}";
        private static string T(string s) => (s ?? "").Trim().ToUpperInvariant();
    }

    public class DescuentoCambioFiscal
    {
        public string Producto { get; set; }
        public string GrupoProducto { get; set; }
        public string Contacto { get; set; }
        public string Proveedor { get; set; }
        public string ContactoProveedor { get; set; }
        public short CantidadMinima { get; set; }
        public decimal Descuento { get; set; }
        public decimal? Precio { get; set; }
        public string Familia { get; set; }
        public string FiltroProducto { get; set; }
        public byte AudienciaOferta { get; set; }
        public decimal? DescuentoPublico { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string Campana { get; set; }
        public string SubGrupoProducto { get; set; }

        /// <summary>A qué se aplica: dos filas con la misma clave en el mismo cliente se pisarían (el motor no desempata).</summary>
        internal string Clave => string.Join("|", T(Producto), T(Familia), T(GrupoProducto), T(SubGrupoProducto), T(FiltroProducto),
            T(Proveedor), T(Contacto), CantidadMinima.ToString(CultureInfo.InvariantCulture));

        internal bool MismoValor(DescuentoCambioFiscal otro) => Math.Round(Descuento, 4) == Math.Round(otro.Descuento, 4)
            && Math.Round(Precio ?? -1, 4) == Math.Round(otro.Precio ?? -1, 4);

        internal DescuentoCambioFiscal ParaContacto(string contacto)
        {
            DescuentoCambioFiscal copia = (DescuentoCambioFiscal)MemberwiseClone();
            copia.Contacto = contacto;
            return copia;
        }

        private static string T(string s) => (s ?? "").Trim().ToUpperInvariant();
    }

    public class OfertaCambioFiscal
    {
        public string Producto { get; set; }
        public string Familia { get; set; }
        public short CantidadConPrecio { get; set; }
        public short CantidadRegalo { get; set; }
        public bool Denegar { get; set; }
        public string Contacto { get; set; }
        public string FiltroProducto { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string SubGrupo { get; set; }

        internal string Clave => string.Join("|", T(Producto), T(Familia), T(FiltroProducto), T(SubGrupo), T(Contacto),
            CantidadConPrecio.ToString(CultureInfo.InvariantCulture), CantidadRegalo.ToString(CultureInfo.InvariantCulture), Denegar ? "1" : "0");

        internal OfertaCambioFiscal ParaContacto(string contacto)
        {
            OfertaCambioFiscal copia = (OfertaCambioFiscal)MemberwiseClone();
            copia.Contacto = contacto;
            return copia;
        }

        private static string T(string s) => (s ?? "").Trim().ToUpperInvariant();
    }

    /// <summary>NestoAPI#876: todo lo de un cliente que el cambio de forma fiscal puede copiar.</summary>
    public class FotoClienteCambioFiscal
    {
        public string Cliente { get; set; }
        public List<FichaCambioFiscal> Fichas { get; set; } = new List<FichaCambioFiscal>();
        public List<VendedorGrupoCambioFiscal> VendedoresGrupo { get; set; } = new List<VendedorGrupoCambioFiscal>();
        public List<CondPagoCambioFiscal> Condiciones { get; set; } = new List<CondPagoCambioFiscal>();
        public List<DescuentoCambioFiscal> Descuentos { get; set; } = new List<DescuentoCambioFiscal>();
        public List<OfertaCambioFiscal> Ofertas { get; set; } = new List<OfertaCambioFiscal>();

        public bool Existe => Fichas.Any();

        public FichaCambioFiscal Principal => Fichas.FirstOrDefault(f => f.ClientePrincipal) ?? Fichas.OrderBy(f => f.Contacto).FirstOrDefault();
    }

    /// <summary>NestoAPI#876: lo que cambia en una ficha del cliente nuevo. Null = no se toca.</summary>
    public class CambioFichaCambioFiscal
    {
        public string Contacto { get; set; }
        public short? Estado { get; set; }
        public string Vendedor { get; set; }
        public string Iva { get; set; }
        public string Comentarios { get; set; }
        public string DiasEnServir { get; set; }
        public string Ruta { get; set; }
        public string PeriodoFacturacion { get; set; }
        public decimal? NoComisiona { get; set; }
        public bool? MantenerJunto { get; set; }

        public bool HayAlgo => Estado.HasValue || Vendedor != null || Iva != null || Comentarios != null || DiasEnServir != null
            || Ruta != null || PeriodoFacturacion != null || NoComisiona.HasValue || MantenerJunto.HasValue;
    }

    /// <summary>NestoAPI#876: lo que hay que escribir para que el cliente nuevo tenga lo del anterior (y lo que se enseña).</summary>
    public class PlanCopiaCambioFiscal
    {
        public List<BloqueCambioFiscalDTO> Bloques { get; set; } = new List<BloqueCambioFiscalDTO>();
        public List<CambioFichaCambioFiscal> Fichas { get; set; } = new List<CambioFichaCambioFiscal>();
        public List<VendedorGrupoCambioFiscal> VendedoresGrupo { get; set; } = new List<VendedorGrupoCambioFiscal>();
        public List<CondPagoCambioFiscal> CondicionesAQuitar { get; set; } = new List<CondPagoCambioFiscal>();
        public List<CondPagoCambioFiscal> CondicionesAPoner { get; set; } = new List<CondPagoCambioFiscal>();
        public List<DescuentoCambioFiscal> Descuentos { get; set; } = new List<DescuentoCambioFiscal>();
        public List<OfertaCambioFiscal> Ofertas { get; set; } = new List<OfertaCambioFiscal>();

        public bool HayCambios => Bloques.Any(b => b.HayCambios);

        /// <summary>Cambia algo de la ficha (Clientes): hay que publicar el cliente a Odoo.</summary>
        public bool CambiaFicha => Fichas.Any(f => f.HayAlgo);
    }

    /// <summary>
    /// NestoAPI#876 (Carlos 26/11/26): «al crear un cliente porque otro ha cambiado los datos, se copian todos los datos; así,
    /// aunque se recalcule un pedido al pasarlo al cliente nuevo, no cambia nada. Si algún dato el usuario pide no copiarlo,
    /// al recalcular sí va a cambiar». LA REGLA, pura: qué se copia de cada bloque (<see cref="BloquesCambioFiscal"/>).
    ///
    /// <para>De qué ficha a cuál: cada ficha viva del nuevo (y su principal) toma la del anterior con el MISMO contacto o, si
    /// no la hay, la principal del anterior. Las filas por contacto (vendedores por grupo, condiciones de pago) igual. Los
    /// descuentos y ofertas con contacto NULL (lo normal) siguen con NULL; con un contacto que el nuevo no tiene, no se copian.</para>
    ///
    /// <para>Idempotente: se calcula sobre lo que hay; lo que el nuevo ya tiene igual no cambia, y una segunda pasada no hace nada.
    /// Nunca lo fiscal (NIF, nombre), ni el IBAN ni el mandato (la cuenta nueva la pone el alta).</para>
    /// </summary>
    public static class ReglaCopiaCambioFiscal
    {
        public const string ORIGEN_CAMBIO_FISCAL = ReglaClienteAnterior.ORIGEN_CAMBIO_FISCAL;

        public static PlanCopiaCambioFiscal Planificar(FotoClienteCambioFiscal anterior, FotoClienteCambioFiscal nuevo, IEnumerable<string> bloques, DateTime hoy)
        {
            PlanCopiaCambioFiscal plan = new PlanCopiaCambioFiscal();
            List<string> pedidos = (bloques ?? BloquesCambioFiscal.TODOS).ToList();
            if (anterior?.Principal == null || nuevo?.Principal == null)
            {
                return plan;
            }
            List<FichaCambioFiscal> destinos = nuevo.Fichas.Where(f => f.ClientePrincipal || (f.Estado ?? 0) >= 0).OrderBy(f => f.Contacto).ToList();
            Dictionary<string, CambioFichaCambioFiscal> cambiosFicha = destinos.ToDictionary(d => d.Contacto, d => new CambioFichaCambioFiscal { Contacto = d.Contacto });

            foreach (string bloque in BloquesCambioFiscal.TODOS.Where(pedidos.Contains))
            {
                BloqueCambioFiscalDTO dto = new BloqueCambioFiscalDTO { Bloque = bloque, Nombre = BloquesCambioFiscal.Nombre(bloque) };
                switch (bloque)
                {
                    case BloquesCambioFiscal.COMERCIAL:
                        Comercial(anterior, nuevo, destinos, cambiosFicha, plan, dto);
                        break;
                    case BloquesCambioFiscal.FISCALIDAD:
                        Fiscalidad(anterior, nuevo, destinos, cambiosFicha, dto);
                        break;
                    case BloquesCambioFiscal.CONDICIONES_PAGO:
                        CondicionesPago(anterior, nuevo, destinos, plan, dto);
                        break;
                    case BloquesCambioFiscal.DESCUENTOS:
                        Descuentos(anterior, nuevo, destinos, plan, dto, hoy);
                        break;
                    case BloquesCambioFiscal.OFERTAS:
                        Ofertas(anterior, nuevo, destinos, plan, dto, hoy);
                        break;
                    case BloquesCambioFiscal.FICHA:
                        Ficha(anterior, nuevo, destinos, cambiosFicha, dto);
                        break;
                }
                plan.Bloques.Add(dto);
            }
            plan.Fichas = cambiosFicha.Values.Where(c => c.HayAlgo).ToList();
            return plan;
        }

        /// <summary>La ficha del anterior de la que copia una ficha del nuevo: la del mismo contacto o la principal.</summary>
        internal static FichaCambioFiscal Origen(FotoClienteCambioFiscal anterior, FichaCambioFiscal destino)
        {
            return anterior.Fichas.FirstOrDefault(f => Igual(f.Contacto, destino.Contacto)) ?? anterior.Principal;
        }

        private static void Comercial(FotoClienteCambioFiscal anterior, FotoClienteCambioFiscal nuevo, List<FichaCambioFiscal> destinos,
            Dictionary<string, CambioFichaCambioFiscal> cambios, PlanCopiaCambioFiscal plan, BloqueCambioFiscalDTO dto)
        {
            dto.Anterior.AddRange(anterior.Fichas.OrderByDescending(f => f.ClientePrincipal).ThenBy(f => f.Contacto)
                .Select(f => $"Contacto {T(f.Contacto)}: estado {f.Estado?.ToString() ?? "(vacío)"}, vendedor {T(f.Vendedor, "(ninguno)")}"));
            dto.Anterior.AddRange(anterior.VendedoresGrupo.Select(v => $"Contacto {T(v.Contacto)}, grupo {T(v.GrupoProducto)}: vendedor {T(v.Vendedor)}"));
            dto.Nuevo.AddRange(nuevo.Fichas.OrderByDescending(f => f.ClientePrincipal).ThenBy(f => f.Contacto)
                .Select(f => $"Contacto {T(f.Contacto)}: estado {f.Estado?.ToString() ?? "(vacío)"}, vendedor {T(f.Vendedor, "(ninguno)")}"));
            dto.Nuevo.AddRange(nuevo.VendedoresGrupo.Select(v => $"Contacto {T(v.Contacto)}, grupo {T(v.GrupoProducto)}: vendedor {T(v.Vendedor)}"));

            foreach (FichaCambioFiscal destino in destinos)
            {
                FichaCambioFiscal origen = Origen(anterior, destino);
                if ((origen.Estado ?? 0) < 0)
                {
                    Avisar(dto, $"El contacto {T(origen.Contacto)} del cliente {T(anterior.Cliente)} está de baja (estado {origen.Estado}): su estado no se copia.");
                }
                else if (origen.Estado.HasValue && origen.Estado != destino.Estado)
                {
                    cambios[destino.Contacto].Estado = origen.Estado;
                    dto.Cambios.Add($"Contacto {T(destino.Contacto)}: estado {destino.Estado?.ToString() ?? "(vacío)"} → {origen.Estado}");
                }
                if (!string.IsNullOrWhiteSpace(origen.Vendedor) && !Igual(origen.Vendedor, destino.Vendedor))
                {
                    cambios[destino.Contacto].Vendedor = origen.Vendedor.Trim();
                    dto.Cambios.Add($"Contacto {T(destino.Contacto)}: vendedor {T(destino.Vendedor, "(ninguno)")} → {origen.Vendedor.Trim()}");
                }
                foreach (VendedorGrupoCambioFiscal grupo in anterior.VendedoresGrupo.Where(v => Igual(v.Contacto, origen.Contacto)))
                {
                    VendedorGrupoCambioFiscal actual = nuevo.VendedoresGrupo.FirstOrDefault(v => Igual(v.Contacto, destino.Contacto) && Igual(v.GrupoProducto, grupo.GrupoProducto));
                    if (actual != null && Igual(actual.Vendedor, grupo.Vendedor) && actual.Estado == grupo.Estado)
                    {
                        continue;
                    }
                    plan.VendedoresGrupo.Add(new VendedorGrupoCambioFiscal
                    {
                        Contacto = destino.Contacto,
                        GrupoProducto = grupo.GrupoProducto?.Trim(),
                        Vendedor = grupo.Vendedor?.Trim(),
                        Estado = grupo.Estado
                    });
                    dto.Cambios.Add($"Contacto {T(destino.Contacto)}, grupo {T(grupo.GrupoProducto)}: vendedor {T(actual?.Vendedor, "(ninguno)")} → {T(grupo.Vendedor)}");
                }
            }
        }

        private static void Fiscalidad(FotoClienteCambioFiscal anterior, FotoClienteCambioFiscal nuevo, List<FichaCambioFiscal> destinos,
            Dictionary<string, CambioFichaCambioFiscal> cambios, BloqueCambioFiscalDTO dto)
        {
            dto.Anterior.AddRange(anterior.Fichas.OrderByDescending(f => f.ClientePrincipal).ThenBy(f => f.Contacto).Select(f => $"Contacto {T(f.Contacto)}: IVA {T(f.Iva, "(sin IVA)")}"));
            dto.Nuevo.AddRange(nuevo.Fichas.OrderByDescending(f => f.ClientePrincipal).ThenBy(f => f.Contacto).Select(f => $"Contacto {T(f.Contacto)}: IVA {T(f.Iva, "(sin IVA)")}"));
            foreach (FichaCambioFiscal destino in destinos)
            {
                FichaCambioFiscal origen = Origen(anterior, destino);
                if (string.IsNullOrWhiteSpace(origen.Iva))
                {
                    // Un cliente sin IVA va a la empresa espejo: eso no se hereda sin que alguien lo decida
                    Avisar(dto, $"El contacto {T(origen.Contacto)} del cliente {T(anterior.Cliente)} no tiene tipo de IVA: no se copia (póngalo a mano si hace falta).");
                }
                else if (!Igual(origen.Iva, destino.Iva))
                {
                    cambios[destino.Contacto].Iva = origen.Iva.Trim();
                    dto.Cambios.Add($"Contacto {T(destino.Contacto)}: IVA {T(destino.Iva, "(sin IVA)")} → {origen.Iva.Trim()}");
                }
            }
        }

        private static void CondicionesPago(FotoClienteCambioFiscal anterior, FotoClienteCambioFiscal nuevo, List<FichaCambioFiscal> destinos,
            PlanCopiaCambioFiscal plan, BloqueCambioFiscalDTO dto)
        {
            dto.Anterior.AddRange(anterior.Condiciones.OrderBy(c => c.Contacto).ThenBy(c => c.ImporteMinimo).Select(Describir));
            dto.Nuevo.AddRange(nuevo.Condiciones.OrderBy(c => c.Contacto).ThenBy(c => c.ImporteMinimo).Select(Describir));
            foreach (FichaCambioFiscal destino in destinos)
            {
                FichaCambioFiscal origen = Origen(anterior, destino);
                List<CondPagoCambioFiscal> deseadas = anterior.Condiciones.Where(c => Igual(c.Contacto, origen.Contacto)).ToList();
                if (!deseadas.Any())
                {
                    Avisar(dto, $"El contacto {T(origen.Contacto)} del cliente {T(anterior.Cliente)} no tiene condiciones de pago: no se copian.");
                    continue;
                }
                List<CondPagoCambioFiscal> actuales = nuevo.Condiciones.Where(c => Igual(c.Contacto, destino.Contacto)).ToList();
                foreach (CondPagoCambioFiscal quitar in actuales.Where(a => deseadas.All(d => d.Clave != a.Clave)))
                {
                    plan.CondicionesAQuitar.Add(quitar);
                    dto.Cambios.Add($"Contacto {T(destino.Contacto)}: se quita {DescribirSinContacto(quitar)}");
                }
                foreach (CondPagoCambioFiscal poner in deseadas.Where(d => actuales.All(a => a.Clave != d.Clave)))
                {
                    plan.CondicionesAPoner.Add(new CondPagoCambioFiscal
                    {
                        Contacto = destino.Contacto,
                        FormaPago = poner.FormaPago?.Trim(),
                        PlazosPago = poner.PlazosPago?.Trim(),
                        ImporteMinimo = poner.ImporteMinimo,
                        CccObligatorio = poner.CccObligatorio
                    });
                    dto.Cambios.Add($"Contacto {T(destino.Contacto)}: se pone {DescribirSinContacto(poner)}");
                }
                CondPagoCambioFiscal conCuenta = deseadas.FirstOrDefault(d => d.CccObligatorio);
                if (conCuenta != null && string.IsNullOrWhiteSpace(destino.Ccc))
                {
                    Avisar(dto, $"La forma de pago {T(conCuenta.FormaPago)} necesita cuenta bancaria y el contacto {T(destino.Contacto)} del cliente {T(nuevo.Cliente)} " +
                        "no tiene: póngasela (la cuenta y el mandato no se copian).");
                }
            }
        }

        private static void Descuentos(FotoClienteCambioFiscal anterior, FotoClienteCambioFiscal nuevo, List<FichaCambioFiscal> destinos,
            PlanCopiaCambioFiscal plan, BloqueCambioFiscalDTO dto, DateTime hoy)
        {
            dto.Anterior.AddRange(anterior.Descuentos.Select(Describir));
            dto.Nuevo.AddRange(nuevo.Descuentos.Select(Describir));
            int caducados = 0;
            HashSet<string> yaPuestas = new HashSet<string>();
            foreach (DescuentoCambioFiscal fila in anterior.Descuentos)
            {
                if (fila.FechaHasta.HasValue && fila.FechaHasta.Value.Date < hoy.Date)
                {
                    caducados++;
                    continue;
                }
                if (!ContactoDestino(fila.Contacto, destinos, out string contacto))
                {
                    Avisar(dto, $"{Describir(fila)}: es del contacto {T(fila.Contacto)}, que el cliente {T(nuevo.Cliente)} no tiene; no se copia.");
                    continue;
                }
                DescuentoCambioFiscal copia = fila.ParaContacto(contacto);
                DescuentoCambioFiscal actual = nuevo.Descuentos.FirstOrDefault(d => d.Clave == copia.Clave);
                if (actual != null)
                {
                    if (!actual.MismoValor(copia))
                    {
                        Avisar(dto, $"{Describir(copia)}: el cliente {T(nuevo.Cliente)} ya tiene otro ({Valor(actual)}); no se toca.");
                    }
                    continue;
                }
                if (!yaPuestas.Add(copia.Clave))
                {
                    continue;
                }
                plan.Descuentos.Add(copia);
                dto.Cambios.Add($"Se pone {Describir(copia)}");
            }
            if (caducados > 0)
            {
                Avisar(dto, caducados == 1 ? "Un descuento del anterior ya ha caducado: no se copia." : $"{caducados} descuentos del anterior ya han caducado: no se copian.");
            }
        }

        private static void Ofertas(FotoClienteCambioFiscal anterior, FotoClienteCambioFiscal nuevo, List<FichaCambioFiscal> destinos,
            PlanCopiaCambioFiscal plan, BloqueCambioFiscalDTO dto, DateTime hoy)
        {
            dto.Anterior.AddRange(anterior.Ofertas.Select(Describir));
            dto.Nuevo.AddRange(nuevo.Ofertas.Select(Describir));
            int caducadas = 0;
            HashSet<string> yaPuestas = new HashSet<string>();
            foreach (OfertaCambioFiscal fila in anterior.Ofertas)
            {
                if (fila.FechaHasta.HasValue && fila.FechaHasta.Value.Date < hoy.Date)
                {
                    caducadas++;
                    continue;
                }
                if (!ContactoDestino(fila.Contacto, destinos, out string contacto))
                {
                    Avisar(dto, $"{Describir(fila)}: es del contacto {T(fila.Contacto)}, que el cliente {T(nuevo.Cliente)} no tiene; no se copia.");
                    continue;
                }
                OfertaCambioFiscal copia = fila.ParaContacto(contacto);
                if (nuevo.Ofertas.Any(o => o.Clave == copia.Clave) || !yaPuestas.Add(copia.Clave))
                {
                    continue;
                }
                plan.Ofertas.Add(copia);
                dto.Cambios.Add($"Se pone {Describir(copia)}");
            }
            if (caducadas > 0)
            {
                Avisar(dto, caducadas == 1 ? "Una oferta del anterior ya ha caducado: no se copia." : $"{caducadas} ofertas del anterior ya han caducado: no se copian.");
            }
        }

        private static void Ficha(FotoClienteCambioFiscal anterior, FotoClienteCambioFiscal nuevo, List<FichaCambioFiscal> destinos,
            Dictionary<string, CambioFichaCambioFiscal> cambios, BloqueCambioFiscalDTO dto)
        {
            dto.Anterior.AddRange(anterior.Fichas.OrderByDescending(f => f.ClientePrincipal).ThenBy(f => f.Contacto).Select(DescribirFicha));
            dto.Nuevo.AddRange(nuevo.Fichas.OrderByDescending(f => f.ClientePrincipal).ThenBy(f => f.Contacto).Select(DescribirFicha));
            foreach (FichaCambioFiscal destino in destinos)
            {
                FichaCambioFiscal origen = Origen(anterior, destino);
                CambioFichaCambioFiscal c = cambios[destino.Contacto];
                string quien = $"Contacto {T(destino.Contacto)}";
                if (!string.IsNullOrWhiteSpace(origen.Comentarios) && !Igual(origen.Comentarios, destino.Comentarios))
                {
                    if (string.IsNullOrWhiteSpace(destino.Comentarios))
                    {
                        c.Comentarios = origen.Comentarios.Trim();
                        dto.Cambios.Add($"{quien}: comentarios copiados");
                    }
                    else
                    {
                        Avisar(dto, $"{quien}: ya tiene comentarios propios; no se tocan (los del anterior: «{Recortar(origen.Comentarios, 80)}»).");
                    }
                }
                if (!string.IsNullOrWhiteSpace(origen.DiasEnServir) && !Igual(origen.DiasEnServir, destino.DiasEnServir))
                {
                    c.DiasEnServir = origen.DiasEnServir.Trim();
                    dto.Cambios.Add($"{quien}: días de servir {T(destino.DiasEnServir)} → {origen.DiasEnServir.Trim()}");
                }
                if (!string.IsNullOrWhiteSpace(origen.Ruta) && !Igual(origen.Ruta, destino.Ruta))
                {
                    c.Ruta = origen.Ruta.Trim();
                    dto.Cambios.Add($"{quien}: ruta {T(destino.Ruta)} → {origen.Ruta.Trim()}");
                }
                if (!string.IsNullOrWhiteSpace(origen.PeriodoFacturacion) && !Igual(origen.PeriodoFacturacion, destino.PeriodoFacturacion))
                {
                    c.PeriodoFacturacion = origen.PeriodoFacturacion.Trim();
                    dto.Cambios.Add($"{quien}: periodo de facturación {T(destino.PeriodoFacturacion)} → {origen.PeriodoFacturacion.Trim()}");
                }
                if (origen.NoComisiona != destino.NoComisiona)
                {
                    c.NoComisiona = origen.NoComisiona;
                    dto.Cambios.Add($"{quien}: no comisiona {Porcentaje(destino.NoComisiona)} → {Porcentaje(origen.NoComisiona)}");
                }
                if (origen.MantenerJunto != destino.MantenerJunto)
                {
                    c.MantenerJunto = origen.MantenerJunto;
                    dto.Cambios.Add($"{quien}: facturar al completar {(destino.MantenerJunto ? "sí" : "no")} → {(origen.MantenerJunto ? "sí" : "no")}");
                }
            }
        }

        /// <summary>Contacto de una fila de descuento u oferta en el nuevo: NULL sigue NULL; uno que el nuevo no tiene, no vale.</summary>
        private static bool ContactoDestino(string contacto, List<FichaCambioFiscal> destinos, out string destino)
        {
            destino = null;
            if (string.IsNullOrWhiteSpace(contacto))
            {
                return true;
            }
            FichaCambioFiscal ficha = destinos.FirstOrDefault(d => Igual(d.Contacto, contacto));
            destino = ficha?.Contacto;
            return ficha != null;
        }

        #region Textos

        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");

        internal static string Describir(CondPagoCambioFiscal c) => $"Contacto {T(c.Contacto)}: {DescribirSinContacto(c)}";

        private static string DescribirSinContacto(CondPagoCambioFiscal c) => $"{T(c.FormaPago)} {T(c.PlazosPago)} (desde {c.ImporteMinimo.ToString("N2", ES)} €)";

        internal static string Describir(DescuentoCambioFiscal d)
        {
            List<string> aQue = new List<string>();
            if (!string.IsNullOrWhiteSpace(d.Producto)) aQue.Add($"producto {d.Producto.Trim()}");
            if (!string.IsNullOrWhiteSpace(d.Familia)) aQue.Add($"familia {d.Familia.Trim()}");
            if (!string.IsNullOrWhiteSpace(d.GrupoProducto)) aQue.Add($"grupo {d.GrupoProducto.Trim()}");
            if (!string.IsNullOrWhiteSpace(d.SubGrupoProducto)) aQue.Add($"subgrupo {d.SubGrupoProducto.Trim()}");
            if (!string.IsNullOrWhiteSpace(d.FiltroProducto)) aQue.Add($"«{d.FiltroProducto.Trim()}»");
            if (!string.IsNullOrWhiteSpace(d.Proveedor)) aQue.Add($"proveedor {d.Proveedor.Trim()}");
            string texto = $"descuento de {(aQue.Any() ? string.Join(" ", aQue) : "todo")}: {Valor(d)}";
            if (d.CantidadMinima > 1) texto += $" desde {d.CantidadMinima} uds.";
            if (!string.IsNullOrWhiteSpace(d.Contacto)) texto += $" (contacto {d.Contacto.Trim()})";
            if (d.FechaHasta.HasValue) texto += $" hasta el {d.FechaHasta.Value:dd/MM/yyyy}";
            return texto;
        }

        private static string Valor(DescuentoCambioFiscal d)
        {
            string precio = d.Precio.HasValue && d.Precio.Value != 0 ? $"precio {d.Precio.Value.ToString("N2", ES)} €" : null;
            string descuento = d.Descuento != 0 ? Porcentaje(d.Descuento) : null;
            return string.Join(" y ", new[] { precio, descuento }.Where(x => x != null).DefaultIfEmpty("0 %"));
        }

        internal static string Describir(OfertaCambioFiscal o)
        {
            string aQue = !string.IsNullOrWhiteSpace(o.Producto) ? $"producto {o.Producto.Trim()}"
                : !string.IsNullOrWhiteSpace(o.Familia) ? $"familia {o.Familia.Trim()}" : "todo";
            if (!string.IsNullOrWhiteSpace(o.SubGrupo)) aQue += $" subgrupo {o.SubGrupo.Trim()}";
            if (!string.IsNullOrWhiteSpace(o.FiltroProducto)) aQue += $" «{o.FiltroProducto.Trim()}»";
            string texto = o.Denegar ? $"oferta denegada en {aQue}" : $"oferta {o.CantidadConPrecio}+{o.CantidadRegalo} en {aQue}";
            if (!string.IsNullOrWhiteSpace(o.Contacto)) texto += $" (contacto {o.Contacto.Trim()})";
            if (o.FechaHasta.HasValue) texto += $" hasta el {o.FechaHasta.Value:dd/MM/yyyy}";
            return texto;
        }

        private static string DescribirFicha(FichaCambioFiscal f) =>
            $"Contacto {T(f.Contacto)}: días de servir {T(f.DiasEnServir)}, ruta {T(f.Ruta)}, periodo {T(f.PeriodoFacturacion)}, " +
            $"no comisiona {Porcentaje(f.NoComisiona)}, facturar al completar {(f.MantenerJunto ? "sí" : "no")}, " +
            (string.IsNullOrWhiteSpace(f.Comentarios) ? "sin comentarios" : $"comentarios «{Recortar(f.Comentarios, 80)}»");

        private static string Porcentaje(decimal tantoPorUno) => (tantoPorUno * 100).ToString("0.##", ES) + " %";

        private static string Recortar(string texto, int maximo)
        {
            string limpio = (texto ?? "").Trim().Replace("\r", " ").Replace("\n", " ");
            return limpio.Length <= maximo ? limpio : limpio.Substring(0, maximo) + "…";
        }

        private static void Avisar(BloqueCambioFiscalDTO dto, string aviso)
        {
            if (!dto.Avisos.Contains(aviso))
            {
                dto.Avisos.Add(aviso);
            }
        }

        private static string T(string s, string siVacio = "(vacío)") => string.IsNullOrWhiteSpace(s) ? siVacio : s.Trim();

        internal static bool Igual(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        #endregion

        #region Enlace, permisos y pedidos

        /// <summary>
        /// Si se puede copiar del anterior al nuevo: tienen que ser «el mismo cliente con otra forma fiscal». Vale si ya consta en
        /// ClientesAnteriores o, si no consta nada, si el nuevo tiene origen CAMBIO-FISCAL (entonces se crea el enlace).
        /// </summary>
        /// <param name="anterioresDelNuevo">Los clientes anteriores con los que ya está enlazado el nuevo.</param>
        public static DecisionEnlaceCambioFiscal DecidirEnlace(string anterior, string nuevo, bool anteriorExiste, bool nuevoExiste,
            IEnumerable<string> anterioresDelNuevo, string origenNuevo)
        {
            string a = anterior?.Trim();
            string n = nuevo?.Trim();
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(n))
            {
                return DecisionEnlaceCambioFiscal.Error(400, "Faltan el cliente anterior y el nuevo.");
            }
            if (Igual(a, n))
            {
                return DecisionEnlaceCambioFiscal.Error(400, $"El cliente anterior y el nuevo son el mismo ({a}).");
            }
            if (!anteriorExiste)
            {
                return DecisionEnlaceCambioFiscal.Error(404, $"No existe el cliente {a}.");
            }
            if (!nuevoExiste)
            {
                return DecisionEnlaceCambioFiscal.Error(404, $"No existe el cliente {n}.");
            }
            List<string> enlazados = (anterioresDelNuevo ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            if (enlazados.Any(x => Igual(x, a)))
            {
                return new DecisionEnlaceCambioFiscal { Vale = true, Enlazados = true };
            }
            if (enlazados.Any())
            {
                return DecisionEnlaceCambioFiscal.Error(409, $"El cliente {n} consta como cambio de forma fiscal del {string.Join(", ", enlazados)}, no del {a}. " +
                    "Compruebe los números.");
            }
            if (Igual(origenNuevo, ORIGEN_CAMBIO_FISCAL))
            {
                return new DecisionEnlaceCambioFiscal { Vale = true, CrearEnlace = true };
            }
            return DecisionEnlaceCambioFiscal.Error(409, $"El cliente {n} no consta como el {a} con otra forma fiscal: su origen no es «Cambio de forma fiscal». " +
                "Si lo es, póngale ese origen en la ficha y vuelva a intentarlo.");
        }

        /// <summary>
        /// Quién copia las condiciones o pasa los pedidos: Administración, Dirección e Informática, y el vendedor del cliente
        /// anterior o su jefe de equipo (como <see cref="ValidadorCambioClienteComercial"/>). Null = puede; si no, el porqué de usted.
        /// </summary>
        public static string MotivoSinPermiso(bool esOficina, string vendedorUsuario, IEnumerable<string> equipoUsuario, string clienteAnterior, string vendedorAnterior)
        {
            if (esOficina)
            {
                return null;
            }
            string suyo = vendedorUsuario?.Trim();
            string delCliente = vendedorAnterior?.Trim();
            if (!string.IsNullOrWhiteSpace(suyo) && !string.IsNullOrWhiteSpace(delCliente))
            {
                HashSet<string> conjunto = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { suyo };
                foreach (string v in equipoUsuario ?? Enumerable.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        _ = conjunto.Add(v.Trim());
                    }
                }
                if (conjunto.Contains(delCliente))
                {
                    return null;
                }
            }
            return $"Solo Administración, Dirección, Informática o el vendedor del cliente {clienteAnterior?.Trim()} (o su jefe de equipo) pueden copiar sus " +
                "condiciones al cliente nuevo y pasarle sus pedidos.";
        }

        /// <summary>
        /// Un pedido pendiente del anterior en la lista: si se puede intentar pasar (la misma regla que CambiarCliente, con el
        /// picking admitido) y lo que hay que saber. <paramref name="lineas"/> son TODAS las del pedido.
        /// </summary>
        public static PedidoPendienteCambioFiscalDTO PedidoPendiente(CabPedidoVta cab, List<LinPedidoVta> lineas, int efectos, List<int> envios, int pagosTarjeta)
        {
            List<LinPedidoVta> pendientes = lineas.Where(EsPendiente).ToList();
            List<int> pickings = lineas.Where(l => (l.Picking ?? 0) != 0 && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
                .Select(l => l.Picking ?? 0).Distinct().OrderBy(p => p).ToList();
            string motivo = cab.NotaEntrega
                ? CambioClientePedido.MotivoNoSePuedeNotaEntrega(cab.Número, lineas, envios)
                : CambioClientePedido.MotivoNoSePuede(cab.Número, false, lineas, 0, efectos, envios, pagosTarjeta, admitirPicking: true);
            bool sePuede = motivo == null;
            if (sePuede && pickings.Any() && !cab.NotaEntrega)
            {
                motivo = "Tiene picking: solo se puede pasar si con el cliente nuevo no cambia ningún precio, descuento, IVA ni importe (el ensayo lo dice).";
            }
            return new PedidoPendienteCambioFiscalDTO
            {
                Empresa = cab.Empresa?.Trim(),
                Pedido = cab.Número,
                Contacto = cab.Contacto?.Trim(),
                Fecha = cab.Fecha,
                Importe = pendientes.Sum(l => l.Base_Imponible),
                Total = pendientes.Sum(l => l.Total),
                Lineas = pendientes.Count,
                TienePicking = pickings.Any(),
                Pickings = pickings,
                NotaEntrega = cab.NotaEntrega,
                SePuedePasar = sePuede,
                Motivo = motivo
            };
        }

        /// <summary>Línea pendiente: -1..1, con cantidad y sin albarán (presupuestos fuera).</summary>
        public static bool EsPendiente(LinPedidoVta l) => l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO
            && (l.Cantidad ?? 0) != 0 && l.Nº_Albarán == null;

        #endregion
    }

    public class DecisionEnlaceCambioFiscal
    {
        public bool Vale { get; set; }
        public bool Enlazados { get; set; }
        public bool CrearEnlace { get; set; }
        /// <summary>400, 404 o 409 cuando no vale.</summary>
        public int Estado { get; set; }
        public string Motivo { get; set; }

        public static DecisionEnlaceCambioFiscal Error(int estado, string motivo) => new DecisionEnlaceCambioFiscal { Estado = estado, Motivo = motivo };
    }
}
