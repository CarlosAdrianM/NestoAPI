using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#727: facturación de fin de mes en la API (sustituye a prdAgruparAlbaranesVta + el paso del Nesto viejo).
    /// Por cada cliente: elige destino, reúne en él los albaranes hasta el corte de todos sus contactos (motor común con la
    /// agrupación por P.O.), lo apunta en Modificaciones y factura el destino con <see cref="IServicioFacturas.CrearFactura"/>.
    /// El motor marca el destino <c>Agrupada = true</c>, que es lo que hace que CrearFactura deje de contestar «FDM» y facture.
    /// </summary>
    public class ServicioFacturarFinDeMes : IServicioFacturarFinDeMes
    {
        internal const string TABLA_MODIFICACIONES = "Pedidos";

        private readonly NVEntities db;
        private readonly IEstrategiaAgrupacionFinDeMes estrategia;
        private readonly IMotorAgrupacionPedidos motor;
        private readonly IServicioFacturas servicioFacturas;

        public ServicioFacturarFinDeMes(
            NVEntities db,
            IEstrategiaAgrupacionFinDeMes estrategia,
            IMotorAgrupacionPedidos motor,
            IServicioFacturas servicioFacturas)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.estrategia = estrategia ?? throw new ArgumentNullException(nameof(estrategia));
            this.motor = motor ?? throw new ArgumentNullException(nameof(motor));
            this.servicioFacturas = servicioFacturas ?? throw new ArgumentNullException(nameof(servicioFacturas));
        }

        /// <summary>
        /// NestoAPI#723: ¿exige el cliente el P.O.? Los pedidos de fin de mes no lo llevan (los que lo llevan van por P.O.), así
        /// que CrearFactura los rechazaría DESPUÉS de reunirlos: se salta el cliente antes de tocar nada. Se fakea en los tests.
        /// </summary>
        internal Func<string, string, bool> ClienteExigeSuPedido { get; set; } =
            (empresa, cliente) => new Clientes.RepositorioSuPedidoObligatorio().ExigeSincronoSeguro(empresa, cliente);

        public async Task<ResultadoFacturacionFinDeMes> Facturar(string empresa, DateTime fechaCorte, string cliente, string contacto, string usuario)
        {
            var resultado = new ResultadoFacturacionFinDeMes { Empresa = empresa, FechaCorte = fechaCorte.Date };

            foreach (GrupoPedidosFinDeMes grupo in estrategia.SeleccionarGrupos(empresa, fechaCorte, cliente, contacto).ToList())
            {
                List<int> numeros = (grupo.Pedidos ?? new List<CabPedidoVta>()).Select(p => p.Número).ToList();
                if (!numeros.Any())
                {
                    continue;
                }

                CabPedidoVta destino = null;
                bool reunido = false;
                bool mantenerJuntoQuitado = false;
                try
                {
                    if (ClienteExigeSuPedido(grupo.Empresa, grupo.Cliente))
                    {
                        Saltar(resultado, grupo, numeros, $"El cliente {grupo.Cliente} exige el número de pedido (P.O.) y estos pedidos de fin de mes no lo tienen: póngaselo y se facturarán por P.O.");
                        continue;
                    }

                    // Sin IVA el pedido se factura en la empresa espejo, previo traspaso; CrearFactura con el db compartido no
                    // traspasa (eso lo hace el gestor de rutas) y además no se pueden mezclar en una factura con los que sí
                    // llevan IVA. Se salta el cliente antes de tocar nada (ElegirDestino puede cambiar el contacto).
                    List<int> sinIva = grupo.Pedidos
                        .Where(p => Traspasos.ServicioTraspasoEmpresa.VaAEmpresaEspejo(p.IVA))
                        .Select(p => p.Número)
                        .ToList();
                    if (sinIva.Any())
                    {
                        Saltar(resultado, grupo, numeros, $"Los pedidos {string.Join(", ", sinIva)} del cliente {grupo.Cliente} no tienen IVA (van a la empresa espejo): el fin de mes de este cliente hay que hacerlo desde Nesto.");
                        continue;
                    }

                    destino = estrategia.ElegirDestino(grupo, fechaCorte);
                    if (destino == null)
                    {
                        Saltar(resultado, grupo, numeros, $"Todos los pedidos del cliente {grupo.Cliente} tienen albaranes posteriores al {fechaCorte:dd/MM/yyyy}: no hay ningún pedido en el que reunir solo los de este mes. Facture los albaranes posteriores o hágalo a mano.");
                        continue;
                    }

                    string contactoAnterior = destino.Contacto;
                    // Lo que se mueve al destino (mismo criterio que el motor) y lo que el destino ya tenía en el corte.
                    List<LinPedidoVta> lineasMovidas = grupo.Pedidos
                        .Where(p => p.Número != destino.Número)
                        .SelectMany(p => (p.LinPedidoVtas ?? new List<LinPedidoVta>()).Where(l => MotorAgrupacionPedidos.LineaAgrupable(l, fechaCorte)))
                        .ToList();
                    var anterior = new
                    {
                        Proceso = "Fin de mes (NestoAPI#727)",
                        FechaCorte = fechaCorte.ToString("yyyy-MM-dd"),
                        Empresa = grupo.Empresa,
                        Cliente = grupo.Cliente,
                        Pedidos = grupo.Pedidos.Select(p => new
                        {
                            Pedido = p.Número,
                            Contacto = p.Número == destino.Número ? contactoAnterior?.Trim() : p.Contacto?.Trim(),
                            LineasAlbaranHastaCorte = (p.LinPedidoVtas ?? new List<LinPedidoVta>())
                                .Where(l => MotorAgrupacionPedidos.LineaAgrupable(l, fechaCorte))
                                .Select(l => l.Nº_Orden).ToList()
                        }).ToList()
                    };
                    // Si al destino le quedan líneas sin servir, no puede quedarse «agrupado»: CrearFactura lo facturaría a
                    // mitad de mes cuando se sirvan. Se le quita la marca después de facturar.
                    bool destinoConPendientes = (destino.LinPedidoVtas ?? new List<LinPedidoVta>())
                        .Any(l => l.Estado == Constantes.EstadosLineaVenta.PENDIENTE || l.Estado == Constantes.EstadosLineaVenta.EN_CURSO);

                    _ = motor.Agrupar(grupo.Pedidos, destino, fechaCorte);

                    // Carlos 30/10/26: en el fin de mes se factura SOLO lo que está en albarán. Si el destino es «al completar»
                    // (MantenerJunto) y le quedan líneas sin servir, prdCrearFacturaVta se niega («tiene marcado el servir
                    // junto…»): se le quita la marca para esta factura y se le vuelve a poner después.
                    if (destinoConPendientes && destino.MantenerJunto)
                    {
                        destino.MantenerJunto = false;
                        mantenerJuntoQuitado = true;
                    }

                    _ = db.Modificaciones.Add(new Modificacion
                    {
                        Tabla = TABLA_MODIFICACIONES,
                        Anterior = JsonConvert.SerializeObject(anterior),
                        Nuevo = JsonConvert.SerializeObject(new
                        {
                            Proceso = "Fin de mes (NestoAPI#727)",
                            PedidoDestino = destino.Número,
                            Contacto = destino.Contacto?.Trim(),
                            PedidosReunidos = numeros,
                            LineasMovidas = lineasMovidas.Select(l => l.Nº_Orden).ToList()
                        }),
                        Usuario = usuario
                    });
                    _ = db.SaveChanges();
                    reunido = true;

                    CrearFacturaResponseDTO factura = await servicioFacturas.CrearFactura(grupo.Empresa, destino.Número, usuario);
                    if (factura == null || string.IsNullOrWhiteSpace(factura.NumeroFactura)
                        || factura.NumeroFactura == Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES)
                    {
                        throw new InvalidOperationException($"No se ha creado la factura del pedido {destino.Número}.");
                    }

                    string aviso = null;
                    if (mantenerJuntoQuitado)
                    {
                        aviso = RestaurarMantenerJunto(factura.Empresa ?? grupo.Empresa, destino.Número);
                        mantenerJuntoQuitado = false;
                    }
                    if (destinoConPendientes)
                    {
                        try
                        {
                            QuitarMarcaAgrupada(factura.Empresa ?? grupo.Empresa, destino.Número);
                        }
                        catch (Exception exMarca)
                        {
                            // La factura ya está hecha: no es un error del cliente, pero hay que quitar la marca a mano.
                            aviso = (aviso == null ? string.Empty : aviso + " ") + $"Al pedido {destino.Número} le quedan líneas sin servir y no se le ha podido quitar la marca de agrupado ({exMarca.Message}): quítesela para que no se facture antes de fin de mes.";
                            ElmahHelper.Log(new Exception($"[Fin de mes #727] {aviso}", exMarca));
                        }
                    }

                    resultado.Facturas.Add(new FacturaFinDeMes
                    {
                        Empresa = factura.Empresa ?? grupo.Empresa,
                        Cliente = grupo.Cliente,
                        PedidoDestino = destino.Número,
                        PedidosReunidos = numeros,
                        LineasMovidas = lineasMovidas.Count,
                        NumeroFactura = factura.NumeroFactura,
                        Aviso = aviso
                    });
                }
                catch (Exception ex)
                {
                    if (!reunido)
                    {
                        // Que lo que no se ha podido grabar de este cliente no se cuele en el SaveChanges del siguiente.
                        DescartarCambiosPendientes();
                    }
                    else if (mantenerJuntoQuitado)
                    {
                        _ = RestaurarMantenerJunto(grupo.Empresa, destino.Número);
                    }
                    string mensaje = reunido
                        ? $"Los albaranes ya están reunidos en el pedido {destino.Número} pero no se ha podido facturar: {ex.Message}. Cuando lo arregle, facture ese pedido desde Nesto."
                        : ex.Message;
                    resultado.Errores.Add(new ErrorFinDeMes
                    {
                        Empresa = grupo.Empresa,
                        Cliente = grupo.Cliente,
                        PedidoDestino = reunido ? destino?.Número : null,
                        Mensaje = mensaje
                    });
                }
            }

            return resultado;
        }

        private static void Saltar(ResultadoFacturacionFinDeMes resultado, GrupoPedidosFinDeMes grupo, List<int> numeros, string motivo)
        {
            resultado.Saltados.Add(new ClienteFinDeMesSaltado
            {
                Empresa = grupo.Empresa,
                Cliente = grupo.Cliente,
                Pedidos = numeros,
                Motivo = motivo
            });
        }

        private void QuitarMarcaAgrupada(string empresa, int numero)
        {
            CabPedidoVta cabecera = db.CabPedidoVtas.FirstOrDefault(c => c.Empresa == empresa && c.Número == numero);
            if (cabecera != null && cabecera.Agrupada)
            {
                cabecera.Agrupada = false;
                _ = db.SaveChanges();
            }
        }

        /// <summary>Vuelve a poner «al completar» (MantenerJunto) al destino. Devuelve un aviso si no se ha podido.</summary>
        private string RestaurarMantenerJunto(string empresa, int numero)
        {
            try
            {
                CabPedidoVta cabecera = db.CabPedidoVtas.FirstOrDefault(c => c.Empresa == empresa && c.Número == numero);
                if (cabecera != null && !cabecera.MantenerJunto)
                {
                    cabecera.MantenerJunto = true;
                    _ = db.SaveChanges();
                }
                return null;
            }
            catch (Exception ex)
            {
                string aviso = $"Al pedido {numero} se le quitó «facturar al completar» para facturar lo albaraneado y no se le ha podido volver a poner ({ex.Message}): póngaselo a mano.";
                ElmahHelper.Log(new Exception($"[Fin de mes #727] {aviso}", ex));
                return aviso;
            }
        }

        private void DescartarCambiosPendientes()
        {
            CambiosPendientesContexto.Descartar(db, "Fin de mes #727");
        }
    }
}
