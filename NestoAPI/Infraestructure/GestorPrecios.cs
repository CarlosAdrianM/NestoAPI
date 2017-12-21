using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public class GestorPrecios
    {
        public static bool calcularAplicarDescuento(Producto producto)
        {
            return calcularAplicarDescuento(producto.Aplicar_Dto, producto.Familia, producto.SubGrupo);
        }
        
        public static bool calcularAplicarDescuento(bool aplicarDescuento, string familia, string subGrupo)
        {
            // Esto podríamos sacarlo a otra clase que sea más fácil de mantener
            // if (!aplicarDescuento || familia == "Ramason" || subGrupo == "ACP")
            if (!aplicarDescuento)
            {
                return false;
            } else
            {
                return true;
            }

        }

        public static void calcularDescuentoProducto(PrecioDescuentoProducto datos)
        {

            DescuentosProducto dtoProducto;

            datos.descuentoCalculado = 0;
            datos.precioCalculado = (decimal)datos.producto.PVP;

            datos.aplicarDescuento = calcularAplicarDescuento(datos.aplicarDescuento, datos.producto.Familia, datos.producto.SubGrupo);

            // En Nesto Viejo, si no tiene el aplicar descuento marcado, solo calcula precios especiales, pero no descuentos
            // Ahora hacemos que no calcule nada, por eso lo pongo aquí arriba.
            if (!datos.aplicarDescuento)
            {
                return;
            }


            using (NVEntities db = new NVEntities()) {
                // AQUÍ CALCULA PRECIOS, NO DESCUENTOS
                //select precio from descuentosproducto with (nolock) where [nº cliente]='15191     ' and contacto='0  ' and [nº producto]= '29487' and empresa='1  ' AND CANTIDADMÍNIMA<=1

                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Contacto == datos.contacto && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad);
                if (dtoProducto != null && dtoProducto.Precio < datos.precioCalculado)
                {
                    datos.precioCalculado = (decimal)dtoProducto.Precio;
                }
                //select precio from descuentosproducto with (nolock) where [nº cliente]='15191     '  and [nº producto]= '29487' and empresa='1  ' AND CantidadMínima<=1
                //select recargopvp from clientes with (nolock) where empresa='1  ' and [nº cliente]='15191     ' and contacto='0  '
                //select top 1 precio,cantidadminima from descuentosproducto where cantidadminíma<=1 and  empresa='1  ' and [Nº Producto]='29352' and [nº cliente]='15191     ' and contacto='0  ' order by cantidadminima desc
                //select top 1 precio,cantidadminima from descuentosproducto where cantidadminíma<=1 and  empresa='1  ' and [Nº Producto]='29352' and [nº cliente]='15191     '  order by cantidadminima desc
                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad);
                if (dtoProducto != null && dtoProducto.Precio < datos.precioCalculado)
                {
                    datos.precioCalculado = (decimal)dtoProducto.Precio;
                }
                //select top 1 precio,cantidadminima from descuentosproducto where cantidadminíma<=1 and  empresa='1  ' and [Nº Producto]='29352' and [nº cliente] is null and [nºproveedor] is null order by cantidadminima desc
                dtoProducto = db.DescuentosProductoes.OrderByDescending(d => d.CantidadMínima).FirstOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad && d.Nº_Cliente == null && d.NºProveedor == null);
                if (dtoProducto != null && dtoProducto.Precio < datos.precioCalculado)
                {
                    datos.precioCalculado = (decimal)dtoProducto.Precio;
                }

                // CALCULA DESCUENTOS
                //select * from descuentosproducto where empresa='1  ' and [nº producto]='29352' and [nº cliente] is null and nºproveedor is null and familia is null
                //select * from descuentosproducto where empresa='1  ' and grupoproducto='PEL' and [nº cliente] is null and  nºproveedor is null and familia is null
                //select * from descuentosproducto where empresa='1  ' and [nº producto]='29352' and [nº cliente]='15191     ' and nºproveedor is null and familia is null
                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.Familia == null);
                if (dtoProducto != null && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }

                //select * from descuentosproducto where empresa='1  ' and grupoproducto='PEL' and [nº cliente]='15191     ' and nºproveedor is null and familia is null

                // AGAIN AND AGAIN AND AGAIN...
                //select isnull(max(descuento),0) from descuentosproducto where [nº cliente]='15191     ' and empresa='1  ' and grupoproducto='PEL' and cantidadmínima<=1 and familia is null and nºproveedor is null
                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Familia == null && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == datos.producto.Grupo);
                if (dtoProducto != null && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }
                //select * from descuentosproducto where empresa='1  ' and familia='Lisap     ' and [nº cliente]='15191     ' and nºproveedor is null and grupoproducto is null
                //select isnull(max(descuento),0) from descuentosproducto where [nº cliente]='15191     ' and empresa='1  ' and familia='Lisap     ' and cantidadmínima<=1 and nºproveedor is null  and grupoproducto is null
                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Familia == datos.producto.Familia && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == null);
                if (dtoProducto != null && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }
                //select * from descuentosproducto where empresa='1  ' and familia='Lisap     ' and [nº cliente]='15191     ' and grupoproducto='PEL' and nºproveedor is null
                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Familia == datos.producto.Familia && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == datos.producto.Grupo);
                if (dtoProducto != null && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }

                if (datos.precioCalculado < datos.producto.PVP * (1 - datos.descuentoCalculado))
                {
                    datos.descuentoCalculado = 0;
                }
                else
                {
                    datos.precioCalculado = (decimal)datos.producto.PVP;
                }

                // Si quisiéramos comprobar también las condiciones que tiene en ficha, descomentar la siguiente línea
                // comprobarCondiciones(datos);
            }
        }

        public static RespuestaValidacion EsOfertaPermitida(Producto producto, PedidoVentaDTO pedido)
        {
            /*
             *  Validaciones a la hora de insertar una oferta permitida:
             *  - No puede tener contacto si no tiene cliente (sí al revés)
             *  - No puede tener familia y producto en blanco. Al menos hay que poner una.
             *  - Hay que crear "Filtro nombre", para poder buscar productos que tengan
             *    un texto ("Esmalte F ", por ejemplo)
             *  - No tiene sentido un precio fijo para toda la familia (son productos diferentes)
             */
            
            if (producto == null || pedido == null)
            {
                return new RespuestaValidacion {
                    ValidacionSuperada = false,
                    OfertaAutorizadaExpresamente = false,
                    Motivo = "El producto o el pedido no existen"
                };
            }

            PrecioDescuentoProducto oferta = MontarOfertaPedido(producto.Número, pedido);

            // Si no tiene ninguna oferta ni descuento, está siempre permitido
            if (oferta.cantidadOferta == 0 && oferta.descuentoReal == 0)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = true,
                    Motivo = "El producto " + producto.Número.Trim() + " no lleva oferta ni descuento",
                    OfertaAutorizadaExpresamente = false
                };
            }

            // Si oferta.cantidad es 0, comprobamos más abajo si se puede o no regalar el producto
            if (oferta.cantidadOferta != 0 && oferta.precioCalculado < producto.PVP && oferta.cantidad > 0)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    Motivo = "Oferta a precio inferior al de ficha en el producto " + producto.Número.Trim()
                };
            }

            if (oferta.cantidadOferta != 0 && oferta.descuentoCalculado > 0) {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    Motivo = "Oferta no puede llevar descuento en el producto " + producto.Número.Trim()
                };
            }

            // oferta.cantidad > 0 es por si solo es una línea de regalo, que entre por descuento
            if (oferta.cantidadOferta > 0 && oferta.cantidad > 0)
            {
                List<OfertaPermitida> ofertas = servicio.BuscarOfertasPermitidas(producto.Número);
                
                // Hacemos el cast (double) para que no sea división entera y 11/5 no de 2
                // Lo que mira es que sea múltiplo de la oferta y que sea mayor (no permite 3+1 
                // si lo autorizado es 6+2, por ejemplo).
                IEnumerable<OfertaPermitida> ofertasFiltradas = ofertas.Where(o =>
                    (o.Cliente == null || o.Cliente == pedido.cliente) &&
                    (o.Contacto == null || o.Cliente == pedido.cliente && o.Contacto == pedido.contacto)
                );

                // Si hay oferta específica para el producto, la cogemos
                IEnumerable<OfertaPermitida> ofertasEspecificasProducto = ofertasFiltradas.Where(o => o.Número == producto.Número.Trim());
                
                if (ofertasEspecificasProducto != null && ofertasEspecificasProducto.Count() > 0)
                {
                    ofertasFiltradas = ofertasEspecificasProducto;
                }

                OfertaPermitida ofertaEncontrada = ofertasFiltradas.FirstOrDefault(o => 
                    ((double)oferta.cantidad / o.CantidadConPrecio == (double)oferta.cantidadOferta / o.CantidadRegalo &&
                    (double)oferta.cantidadOferta / o.CantidadRegalo >= 1)
                    || // para que acepte el 3+1 si está aceptado el 2+1, por ejemplo
                    ((double)oferta.cantidad / oferta.cantidadOferta / o.CantidadRegalo > (double)o.CantidadConPrecio / oferta.cantidadOferta / o.CantidadRegalo)
                );


                // también hay que controlar que denegar == false --> otro test
                if (ofertaEncontrada != null)
                {
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = true,
                        Motivo = "Existe una oferta autorizada expresa de " + oferta.cantidad.ToString() 
                        + "+" + oferta.cantidadOferta.ToString()+ " del producto " + producto.Número.Trim(),
                        OfertaAutorizadaExpresamente = true
                    };
                }

                if (ofertasEspecificasProducto != null && ofertasEspecificasProducto.Count() > 0)
                {
                    OfertaPermitida ofertaEspecifica = ofertasEspecificasProducto.FirstOrDefault();
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = false,
                        Motivo = "La oferta máxima para el producto " + producto.Número.Trim() +
                    " es el " + ofertaEspecifica.CantidadConPrecio.ToString() + "+" + ofertaEspecifica.CantidadRegalo.ToString()
                    };
                }
                
            } else
            {
                // mirar si está en Descuentos producto para ese cliente, familia o producto
                IEnumerable<DescuentosProducto> descuentos = GestorPrecios.servicio.BuscarDescuentosPermitidos(oferta.producto.Número, pedido.cliente, pedido.contacto);

                IEnumerable<DescuentosProducto> descuentosEspecificosProducto = descuentos.Where(d => d.Nº_Producto == oferta.producto.Número);
                // Si hay un descuento específico para el producto, éste prevale sobre el de la familia o grupo
                if (descuentosEspecificosProducto!=null && descuentosEspecificosProducto.Count() > 0)
                {
                    descuentos = descuentosEspecificosProducto;
                }

                DescuentosProducto descuentoAutorizado = descuentos.FirstOrDefault(d => 
                    d.Descuento >= oferta.descuentoReal && oferta.cantidad >= d.CantidadMínima);
                if (descuentoAutorizado != null)
                {
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = true,
                        Motivo = "Hay un descuento autorizado del " + descuentoAutorizado.Descuento.ToString("P2")
                    };
                }

                DescuentosProducto precioAutorizado = descuentos.FirstOrDefault(d => 
                    d.Precio <= oferta.precioCalculado && oferta.cantidad > d.CantidadMínima);
                if (precioAutorizado != null)
                {
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = true,
                        Motivo = "Hay un precio autorizado de " + precioAutorizado.Precio.Value.ToString("C")
                    };
                }

                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    Motivo = "No se encuentra autorizado el descuento del " + oferta.descuentoReal.Value.ToString("P2") 
                        + " para el producto " + producto.Número.Trim()
                };
            }

            return new RespuestaValidacion {
                ValidacionSuperada = false,
                Motivo = "No se encuentra autorización para la oferta del producto " + producto.Número.Trim(),
                OfertaAutorizadaExpresamente = true
            };
        }

        /*
         * Dado un pedido y un producto determinados, nos devuelve la oferta de ese producto que 
         * hay que ese pedido.
         */
        public static PrecioDescuentoProducto MontarOfertaPedido(String numeroProducto, PedidoVentaDTO pedido)
        {
            if (numeroProducto != null)
            {
                numeroProducto = numeroProducto.Trim();
            }

            IEnumerable<LineaPedidoVentaDTO> lineasProducto = pedido.LineasPedido.Where(p => p.producto == numeroProducto);
            if (lineasProducto == null || lineasProducto.Count() == 0)
            {
                return null;
            }

            IEnumerable<LineaPedidoVentaDTO> lineasConPrecio = lineasProducto.Where(l => l.precio != 0);
            IEnumerable<LineaPedidoVentaDTO> lineasSinPrecio = lineasProducto.Where(l => l.precio == 0);

            Producto producto = servicio.BuscarProducto(numeroProducto);


            return new PrecioDescuentoProducto {
                cantidadOferta = (short)lineasSinPrecio.Sum(l => l.cantidad),
                cantidad = (short)lineasConPrecio.Sum(l => l.cantidad),
                producto = producto,
                precioCalculado = (decimal)lineasConPrecio.Select(l => l.precio).DefaultIfEmpty().Average(),
                descuentoCalculado = lineasConPrecio.Select(l => l.descuento + l.descuentoProducto).DefaultIfEmpty().Average()
            };
        }

        private static void cargarListaCondiciones()
        {
            // Rellenamos la lista estática de las condiciones que queremos comprobar.
            // Se implenta de esta forma para que sea sencillo en el futuro poner o 
            // o quitar condiciones, ya que se implementa como clases individuales que
            // implementan el interfaz ICondicionPrecioDescuento
            listaCondiciones.Add(new OtrosAparatosNoPuedeLlevarDescuento());
            listaCondiciones.Add(new DuNoPuedeLlevarCualquierDescuento());
            listaCondiciones.Add(new RamasonNoPuedeLlevarNingunDescuento());
            listaCondiciones.Add(new CerasConPrecioMinimo());
            listaCondiciones.Add(new ThuyaNoPuedeLlevarCualquierDescuento());
        }

        private static void CargarListaValidadoresPedido()
        {
            listaValidadores.Add(new ValidadorOtrosAparatosSiempreSinDescuento());
        }

        public static bool comprobarCondiciones(PrecioDescuentoProducto datos) {
            // Primero miramos si ese precio está en las oferta (tiene metidos los precios generales para todos los clientes)
            if (!datos.descuentosRellenos)
            {
                using (NVEntities db = new NVEntities())
                {
                    // Lo hacemos así para que se pueda rellenar por fuera para los test
                    datos.descuentosProducto = db.DescuentosProductoes.OrderByDescending(d => d.CantidadMínima).Where(d => d.Empresa == datos.producto.Empresa && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad && d.Nº_Cliente == null && d.NºProveedor == null);
                }
            }
            if (datos.descuentosProducto != null)
            {
                DescuentosProducto dtoProducto = datos.descuentosProducto.OrderByDescending(d => d.CantidadMínima).FirstOrDefault(d => d.CantidadMínima <= datos.cantidad);
                if (dtoProducto != null && dtoProducto.Precio * (1 - dtoProducto.Descuento) <= datos.producto.PVP * (1 - datos.descuentoReal))
                {
                    return true;
                }
            }


            // Recorre listaCondiciones y mira una a una si "datos" cumplen todas las condiciones
            // Devuelve true si cumple todas y false si alguna no se cumple
            // Las que no se cumplen son corregidas durante la comprobación
            if (listaCondiciones == null || listaCondiciones.Count == 0)
            {
                cargarListaCondiciones();
            }

            bool cumpleTodasLasCondiciones = true;

            // Cambiar el for por un while para mejorar rendimiento
            foreach (ICondicionPrecioDescuento condicion in listaCondiciones) {
                cumpleTodasLasCondiciones = condicion.precioAceptado(datos) && cumpleTodasLasCondiciones;
            };
            return cumpleTodasLasCondiciones;
        }

        public static List<ICondicionPrecioDescuento> listaCondiciones = new List<ICondicionPrecioDescuento>();

        public static List<IValidadorPedido> listaValidadores = new List<IValidadorPedido>();

        // Es para poder sobreescribirlo en los tests
        public static IServicioPrecios servicio = new ServicioPrecios();

        // Método para validar todas las ofertas de un pedido
        public static RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = true
            };

            if (listaValidadores == null || listaValidadores.Count == 0)
            {
                CargarListaValidadoresPedido();
            }

            foreach (IValidadorPedido validador in listaValidadores)
            {
                respuesta = validador.EsPedidoValido(pedido, servicio);
                if (!respuesta.ValidacionSuperada)
                {
                    break;
                }
            };

            return respuesta;
        }
    }

    public class PrecioDescuentoProducto
    {
        public decimal precioCalculado;
        public decimal descuentoCalculado;
        public Producto producto;
        public string cliente;
        public string contacto;
        public short cantidad;
        public short cantidadOferta;
        public bool aplicarDescuento;
        public string motivo; // cadena para mostrar en la interfaz de usuario
        public decimal? descuentoReal {
            get
            {
                decimal dividendo = (precioCalculado * (1 -descuentoCalculado)  * cantidad);
                decimal divisor = ((decimal)producto.PVP * (cantidad + cantidadOferta));
                return divisor != 0 ? 1 - ( dividendo / divisor ) : 0;
            }
        }
        public decimal precioCalculadoDeFicha
        {
            get
            {
                decimal? precioProducto = producto.PVP;
                decimal? descuentoFinal = 1 - descuentoReal;
                double baseRedondear = (double)(precioProducto * descuentoFinal);
                return (decimal)Math.Round(baseRedondear, 2);
            }

        }
        public bool descuentosRellenos;
        public IQueryable<DescuentosProducto> descuentosProducto;
    }

    public class RespuestaValidacion
    {
        public bool ValidacionSuperada { get; set; }
        public string Motivo { get; set; }
        public bool OfertaAutorizadaExpresamente { get; set; }
    }

    public interface ICondicionPrecioDescuento 
    {
        // Pasamos datos del producto para ver qué precio mínimo se le puede dejar.
        // En caso de ser correctos todos los datos que hemos pasado, el procedimiento
        // devolverá true sin modificar "precio", y en caso de no poder ser esas condiciones
        // devolverá false y se modificarán los campos de "precio" que sea necesario para
        // que se pueda producir la venta cumpliendo nuestras condiciones.
        bool precioAceptado(PrecioDescuentoProducto precio);
    }

    public interface IValidadorPedido
    {
        RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio);
    }

}
