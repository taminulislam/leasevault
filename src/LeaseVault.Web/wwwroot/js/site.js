// Shared UI helpers: initialise plain DataTables on any table marked data-table.
(function ($) {
    $(function () {
        $('table[data-table]').each(function () {
            $(this).DataTable({
                pageLength: 15,
                lengthMenu: [10, 15, 25, 50],
                order: [],
                stateSave: false
            });
        });
    });
})(jQuery);
