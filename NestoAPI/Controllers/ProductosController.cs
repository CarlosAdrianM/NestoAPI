﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Models;
using NestoAPI.Infraestructure;

namespace NestoAPI.Controllers
{
    public class ProductosController : ApiController
    {
        private NVEntities db;

        public ProductosController()
        {
            db = new NVEntities();
        }

        public ProductosController(NVEntities db)
        {
            this.db = db;
        }

        /*
        // GET: api/Productos
        public IQueryable<Producto> GetProductos()
        {
            return db.Productos;
        }
        */

        // GET: api/Productos/5
        [ResponseType(typeof(ProductoPlantillaDTO))]
        public async Task<IHttpActionResult> GetProducto(string empresa, string id)
        {
            Producto producto = await db.Productos.SingleOrDefaultAsync(p => p.Empresa == empresa && p.Número == id);
            if (producto == null)
            {
                return NotFound();
            }

            ProductoPlantillaDTO productoDTO = new ProductoPlantillaDTO()
            {
                producto = producto.Número,
                nombre = producto.Nombre,
                precio = (decimal)producto.PVP,
                aplicarDescuento = producto.Aplicar_Dto
            };

            return Ok(productoDTO);
        }


        // GET: api/Productos/5
        [ResponseType(typeof(ProductoDTO))]
        public async Task<IHttpActionResult> GetProducto(string empresa, string id, bool fichaCompleta)
        {
            Producto producto = await db.Productos.SingleOrDefaultAsync(p => p.Empresa == empresa && p.Número == id);
            if (producto == null)
            {
                return NotFound();
            }

            ProductoDTO productoDTO = new ProductoDTO()
            {
                Producto = producto.Número?.Trim(),
                Nombre = producto.Nombre?.Trim(),
                Tamanno = producto.Tamaño,
                UnidadMedida = producto.UnidadMedida?.Trim(),
                Familia = producto.Familia1.Descripción?.Trim(),
                PrecioProfesional = (decimal)producto.PVP,
                Estado = (short)producto.Estado,
                Grupo = producto.Grupo,
                Subgrupo = producto.SubGruposProducto.Descripción?.Trim()
            };
            
            // Lo dejo medio-hardcoded porque no quiero que los vendedores vean otros almacenes
            if (!producto.Ficticio)
            {
                productoDTO.Stocks.Add(CalcularStockProducto(id, Constantes.Productos.ALMACEN_POR_DEFECTO));
                productoDTO.Stocks.Add(CalcularStockProducto(id, Constantes.Productos.ALMACEN_TIENDA));
            }            

            return Ok(productoDTO);
        }

        private ProductoDTO.StockProducto CalcularStockProducto(string producto, string almacen)
        {
            ProductoDTO.StockProducto stockProducto = new ProductoDTO.StockProducto
            {
                Almacen = almacen,
                Stock = db.ExtractosProducto.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Número == producto).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).Sum(),
                PendienteEntregar = db.LinPedidoVtas.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto == producto && (e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE)).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).Sum(),
                PendienteRecibir = db.LinPedidoCmps.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto == producto && (e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE) && e.Enviado == true).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).Sum(),
                FechaEstimadaRecepcion = (DateTime)db.LinPedidoCmps.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto == producto && (e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE)).Select(e => e.FechaRecepción).DefaultIfEmpty(DateTime.MaxValue).Min(),
                PendienteReposicion = db.PreExtrProductos.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto.Número == producto && e.NºTraspaso != null && e.NºTraspaso > 0).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).Sum()
            };

            return stockProducto;
        }

        // GET: api/Productos/5
        [ResponseType(typeof(ProductoPlantillaDTO))]
        public async Task<IHttpActionResult> GetProducto(string empresa, string id, string cliente, string contacto, short cantidad)
        {
            Producto producto = await db.Productos.SingleOrDefaultAsync(p => p.Empresa == empresa && p.Número == id);
            if (producto == null)
            {
                return NotFound();
            }

            ProductoPlantillaDTO productoDTO = new ProductoPlantillaDTO()
            {
                producto = producto.Número,
                nombre = producto.Nombre,
                precio = (decimal)producto.PVP,
                aplicarDescuento = producto.Aplicar_Dto
            };

            PrecioDescuentoProducto precio = new PrecioDescuentoProducto
            {
                precioCalculado = 0, //lo recalcula el gestor de precios
                descuentoCalculado = 0, //lo recalcula el gestor de precios
                producto = producto,
                cliente = cliente,
                contacto = contacto,
                cantidad = cantidad,
                aplicarDescuento = producto.Aplicar_Dto || cliente == "15191"
            };

            GestorPrecios.calcularDescuentoProducto(precio);
            productoDTO.precio = precio.precioCalculado;
            productoDTO.aplicarDescuento = precio.aplicarDescuento;
            productoDTO.descuento = precio.descuentoCalculado;

            return Ok(productoDTO);
        }
        /*
        // PUT: api/Productos/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutProducto(string id, Producto producto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != producto.Empresa)
            {
                return BadRequest();
            }

            db.Entry(producto).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ProductoExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return StatusCode(HttpStatusCode.NoContent);
        }

        // POST: api/Productos
        [ResponseType(typeof(Producto))]
        public async Task<IHttpActionResult> PostProducto(Producto producto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.Productos.Add(producto);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (ProductoExists(producto.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = producto.Empresa }, producto);
        }

        // DELETE: api/Productos/5
        [ResponseType(typeof(Producto))]
        public async Task<IHttpActionResult> DeleteProducto(string id)
        {
            Producto producto = await db.Productos.FindAsync(id);
            if (producto == null)
            {
                return NotFound();
            }

            db.Productos.Remove(producto);
            await db.SaveChangesAsync();

            return Ok(producto);
        }
        */

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool ProductoExists(string id)
        {
            return db.Productos.Count(e => e.Empresa == id) > 0;
        }
    }
}