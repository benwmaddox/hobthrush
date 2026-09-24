module fixture.route_example;
pub union Reply { Found(Text), Missing }
route GET "/items/{id}" { path id: i32; handler: get_item; response Found: 200 json Text; response Missing: 404; }
